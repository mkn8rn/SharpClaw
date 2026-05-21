import http from 'node:http';

export const protocolVersion = 1;

export const env = {
  moduleDirectory: 'SHARPCLAW_MODULE_DIR',
  moduleDataDirectory: 'SHARPCLAW_MODULE_DATA_DIR',
  controlAddress: 'SHARPCLAW_CONTROL_ADDRESS',
  controlToken: 'SHARPCLAW_CONTROL_TOKEN',
  moduleId: 'SHARPCLAW_MODULE_ID',
  moduleRuntime: 'SHARPCLAW_MODULE_RUNTIME'
};

export const controlPaths = {
  handshake: '/.sharpclaw/handshake',
  discovery: '/.sharpclaw/discovery',
  health: '/.sharpclaw/health',
  initialize: '/.sharpclaw/initialize',
  shutdown: '/.sharpclaw/shutdown'
};

export const tokenHeaderName = 'X-SharpClaw-Control-Token';

export function createSharpClawHost(definition, options = {}) {
  const normalized = normalizeDefinition(definition);
  const runtime = options.runtime ?? 'node';
  const runtimeVersion = options.runtimeVersion ?? process.version;
  const controlAddress = new URL(options.controlAddress ?? readRequiredEnv(env.controlAddress));
  const controlToken = options.controlToken ?? readRequiredEnv(env.controlToken);
  const moduleId = options.moduleId ?? process.env[env.moduleId] ?? normalized.moduleId;
  const compiledEndpoints = normalized.endpoints.map(compileEndpoint);
  let server;

  async function start() {
    if (server) {
      return { address: controlAddress.toString() };
    }

    server = http.createServer((request, response) => {
      handleRequest(request, response).catch(error => {
        writeJson(response, 500, {
          error: error?.message ?? 'Unhandled SharpClaw module host error'
        });
      });
    });

    await new Promise((resolve, reject) => {
      server.once('error', reject);
      server.listen(Number(controlAddress.port), controlAddress.hostname, () => {
        server?.off('error', reject);
        resolve();
      });
    });

    return { address: controlAddress.toString() };
  }

  async function stop() {
    if (!server) {
      return;
    }

    const current = server;
    server = undefined;
    await new Promise(resolve => current.close(resolve));
  }

  async function handleRequest(request, response) {
    const path = new URL(request.url ?? '/', controlAddress).pathname;
    if (!hasExpectedToken(request, controlToken)) {
      writeJson(response, 401, { error: 'Unauthorized' });
      return;
    }

    if (path.startsWith('/.sharpclaw/')) {
      await handleControlRequest(request, response, path);
      return;
    }

    await handleModuleEndpointRequest(request, response, path);
  }

  async function handleControlRequest(request, response, path) {
    if (request.method === 'POST' && path === controlPaths.handshake) {
      writeJson(response, 200, {
        protocolVersion,
        moduleId,
        toolPrefix: normalized.toolPrefix,
        runtime,
        runtimeVersion,
        capabilities: normalized.capabilities
      });
      return;
    }

    if (request.method === 'GET' && path === controlPaths.discovery) {
      writeJson(response, 200, {
        endpoints: normalized.endpoints.map(toDescriptor)
      });
      return;
    }

    if (request.method === 'GET' && path === controlPaths.health) {
      const health = await normalized.health(createContext(request, path, {}));
      writeJson(response, 200, health ?? { isHealthy: true });
      return;
    }

    if (request.method === 'POST' && path === controlPaths.initialize) {
      const message = await normalized.initialize(createContext(request, path, {}));
      writeJson(response, 200, {
        accepted: true,
        message: typeof message === 'string' ? message : undefined
      });
      return;
    }

    if (request.method === 'POST' && path === controlPaths.shutdown) {
      const message = await normalized.shutdown(createContext(request, path, {}));
      writeJson(response, 200, {
        accepted: true,
        message: typeof message === 'string' ? message : undefined
      });
      setImmediate(() => {
        stop().catch(error => {
          console.error('SharpClaw shutdown failed:', error);
          process.exitCode = 1;
        });
      });
      return;
    }

    writeJson(response, 404, { error: 'Unknown SharpClaw control route' });
  }

  async function handleModuleEndpointRequest(request, response, path) {
    const method = request.method?.toUpperCase() ?? 'GET';
    const endpoint = compiledEndpoints.find(candidate =>
      candidate.method === method && candidate.match(path));

    if (!endpoint) {
      writeJson(response, 404, { error: 'Endpoint not found' });
      return;
    }

    const params = endpoint.match(path) ?? {};
    const context = createContext(request, path, params);
    const result = await endpoint.handler(context);
    await writeEndpointResult(response, result);
  }

  return { start, stop };
}

export function json(value, status = 200, headers = {}) {
  return {
    status,
    headers: {
      'Content-Type': 'application/json; charset=utf-8',
      ...headers
    },
    body: JSON.stringify(value)
  };
}

export function text(value, status = 200, headers = {}) {
  return {
    status,
    headers: {
      'Content-Type': 'text/plain; charset=utf-8',
      ...headers
    },
    body: value
  };
}

function normalizeDefinition(definition) {
  if (!definition || typeof definition !== 'object') {
    throw new TypeError('SharpClaw module definition is required.');
  }

  if (!definition.moduleId) {
    throw new TypeError('SharpClaw module definition must include moduleId.');
  }

  if (!definition.toolPrefix) {
    throw new TypeError('SharpClaw module definition must include toolPrefix.');
  }

  const endpoints = Array.isArray(definition.endpoints) ? definition.endpoints : [];
  return {
    moduleId: definition.moduleId,
    toolPrefix: definition.toolPrefix,
    capabilities: definition.capabilities ?? ['endpoints', 'lifecycleHooks'],
    endpoints,
    initialize: definition.initialize ?? noop,
    shutdown: definition.shutdown ?? noop,
    health: definition.health ?? (() => ({ isHealthy: true, message: 'ready' }))
  };
}

function compileEndpoint(endpoint) {
  if (!endpoint || typeof endpoint !== 'object') {
    throw new TypeError('Endpoint descriptors must be objects.');
  }

  if (typeof endpoint.handler !== 'function') {
    throw new TypeError(`Endpoint ${endpoint.routePattern} is missing a handler.`);
  }

  return {
    ...endpoint,
    method: (endpoint.method ?? 'GET').toUpperCase(),
    responseMode: endpoint.responseMode ?? 'json',
    match: compileRoutePattern(endpoint.routePattern),
    handler: endpoint.handler
  };
}

function toDescriptor(endpoint) {
  const {
    method,
    routePattern,
    responseMode,
    authPolicy,
    permission,
    contributionId,
    metadata
  } = endpoint;

  return {
    method: (method ?? 'GET').toUpperCase(),
    routePattern,
    responseMode: responseMode ?? 'json',
    authPolicy,
    permission,
    contributionId,
    metadata
  };
}

function compileRoutePattern(routePattern) {
  if (!routePattern || !routePattern.startsWith('/')) {
    throw new TypeError(`Invalid SharpClaw route pattern '${routePattern}'.`);
  }

  const patternSegments = routePattern.split('/').filter(Boolean);
  return path => {
    const pathSegments = path.split('/').filter(Boolean);
    const params = {};

    for (let i = 0; i < patternSegments.length; i += 1) {
      const pattern = patternSegments[i];
      const value = pathSegments[i];

      if (pattern.startsWith('{**') && pattern.endsWith('}')) {
        params[pattern.slice(3, -1)] = pathSegments.slice(i).join('/');
        return params;
      }

      if (value === undefined) {
        return null;
      }

      if (pattern.startsWith('{') && pattern.endsWith('}')) {
        params[pattern.slice(1, -1)] = decodeURIComponent(value);
        continue;
      }

      if (pattern !== value) {
        return null;
      }
    }

    return pathSegments.length === patternSegments.length ? params : null;
  };
}

function createContext(request, path, params) {
  const parsed = new URL(request.url ?? '/', 'http://127.0.0.1');
  return {
    request,
    method: request.method ?? 'GET',
    path,
    params,
    query: parsed.searchParams,
    headers: request.headers,
    env: {
      moduleDirectory: process.env[env.moduleDirectory],
      moduleDataDirectory: process.env[env.moduleDataDirectory],
      moduleId: process.env[env.moduleId],
      runtime: process.env[env.moduleRuntime]
    },
    readText: () => readText(request),
    readJson: async () => JSON.parse(await readText(request))
  };
}

async function writeEndpointResult(response, result) {
  if (result === undefined || result === null) {
    response.writeHead(204);
    response.end();
    return;
  }

  if (typeof result === 'string' || Buffer.isBuffer(result)) {
    response.writeHead(200);
    response.end(result);
    return;
  }

  if ('body' in result || 'status' in result || 'headers' in result) {
    const status = result.status ?? 200;
    response.writeHead(status, result.headers ?? {});
    response.end(result.body ?? '');
    return;
  }

  writeJson(response, 200, result);
}

function hasExpectedToken(request, controlToken) {
  const actual = request.headers[tokenHeaderName.toLowerCase()];
  if (Array.isArray(actual)) {
    return actual.includes(controlToken);
  }

  return actual === controlToken;
}

function writeJson(response, status, value) {
  const body = JSON.stringify(value);
  response.writeHead(status, {
    'Content-Type': 'application/json; charset=utf-8',
    'Content-Length': Buffer.byteLength(body)
  });
  response.end(body);
}

async function readText(request) {
  const chunks = [];
  for await (const chunk of request) {
    chunks.push(Buffer.from(chunk));
  }

  return Buffer.concat(chunks).toString('utf8');
}

function readRequiredEnv(name) {
  const value = process.env[name];
  if (!value) {
    throw new Error(`Missing required environment variable '${name}'.`);
  }

  return value;
}

function noop() {
}
