import * as vscode from 'vscode';
import WebSocket from 'ws';

let socket: WebSocket | null = null;
let pendingRequests = new Map<string, (data: any) => void>();

export function activate(context: vscode.ExtensionContext) {
    context.subscriptions.push(
        vscode.commands.registerCommand('sharpclaw.connect', connect),
        vscode.commands.registerCommand('sharpclaw.disconnect', disconnect)
    );

    // Auto-connect on startup
    connect();
}

export function deactivate() {
    disconnect();
}

// ═══════════════════════════════════════════════════════════════
// WebSocket connection
// ═══════════════════════════════════════════════════════════════

function connect() {
    if (socket?.readyState === WebSocket.OPEN) {
        vscode.window.showInformationMessage('SharpClaw: Already connected.');
        return;
    }

    const config = vscode.workspace.getConfiguration('sharpclaw');
    const url = config.get<string>('apiUrl', 'ws://127.0.0.1:48923/editor/ws');

    socket = new WebSocket(url);

    socket.on('open', () => {
        // Send registration
        const registration = {
            type: 'register',
            editorType: 'VisualStudioCode',
            editorVersion: vscode.version,
            workspacePath: vscode.workspace.workspaceFolders?.[0]?.uri.fsPath ?? null
        };
        socket!.send(JSON.stringify(registration));
        vscode.window.showInformationMessage('SharpClaw: Connected.');
    });

    socket.on('message', (data: WebSocket.Data) => {
        try {
            const msg = JSON.parse(data.toString());

            if (msg.type === 'registered') {
                console.log(`SharpClaw: Registered as session ${msg.sessionId}`);
                return;
            }

            if (msg.type === 'request') {
                handleRequest(msg);
            }
        } catch (e) {
            console.error('SharpClaw: Failed to parse message', e);
        }
    });

    socket.on('close', () => {
        vscode.window.showWarningMessage('SharpClaw: Disconnected.');
        socket = null;
    });

    socket.on('error', (err) => {
        vscode.window.showErrorMessage(`SharpClaw: Connection error: ${err.message}`);
    });
}

function disconnect() {
    if (socket) {
        socket.close();
        socket = null;
    }
}

function respond(requestId: string, success: boolean, data?: string, error?: string) {
    if (!socket || socket.readyState !== WebSocket.OPEN) return;
    socket.send(JSON.stringify({ type: 'response', requestId, success, data, error }));
}

// ═══════════════════════════════════════════════════════════════
// Request handler — dispatches to VS Code API
// ═══════════════════════════════════════════════════════════════

async function handleRequest(msg: any) {
    const { requestId, action, params } = msg;

    try {
        let result: string;

        switch (action) {
            case 'read_file':
                result = await readFile(params);
                break;
            case 'get_open_files':
                result = await getOpenFiles();
                break;
            case 'get_selection':
                result = await getSelection();
                break;
            case 'get_diagnostics':
                result = await getDiagnostics(params);
                break;
            case 'apply_edit':
                result = await applyEdit(params);
                break;
            case 'create_file':
                result = await createFile(params);
                break;
            case 'delete_file':
                result = await deleteFile(params);
                break;
            case 'show_diff':
                result = await showDiff(params);
                break;
            case 'run_build':
                result = await runBuild();
                break;
            case 'run_terminal':
                result = await runTerminal(params);
                break;
            default:
                respond(requestId, false, undefined, `Unknown action: ${action}`);
                return;
        }

        respond(requestId, true, result);
    } catch (e: any) {
        respond(requestId, false, undefined, e.message ?? String(e));
    }
}

// ═══════════════════════════════════════════════════════════════
// Editor action implementations
// ═══════════════════════════════════════════════════════════════

async function readFile(params: any): Promise<string> {
    const uri = resolveUri(params.filePath);
    const doc = await vscode.workspace.openTextDocument(uri);
    const startLine = (params.startLine ?? 1) - 1;
    const endLine = params.endLine ?? doc.lineCount;
    const range = new vscode.Range(startLine, 0, endLine, 0);
    return doc.getText(range);
}

async function getOpenFiles(): Promise<string> {
    const tabs = vscode.window.tabGroups.all
        .flatMap(g => g.tabs)
        .map(t => {
            const input = t.input as any;
            return input?.uri?.fsPath ?? t.label;
        });
    return JSON.stringify(tabs);
}

async function getSelection(): Promise<string> {
    const editor = vscode.window.activeTextEditor;
    if (!editor) return JSON.stringify({ activeFile: null });

    return JSON.stringify({
        activeFile: editor.document.uri.fsPath,
        language: editor.document.languageId,
        selectionStartLine: editor.selection.start.line + 1,
        selectionEndLine: editor.selection.end.line + 1,
        selectedText: editor.document.getText(editor.selection)
    });
}

async function getDiagnostics(params: any): Promise<string> {
    let diagnostics: [vscode.Uri, readonly vscode.Diagnostic[]][];

    if (params?.filePath) {
        const uri = resolveUri(params.filePath);
        diagnostics = [[uri, vscode.languages.getDiagnostics(uri)]];
    } else {
        diagnostics = vscode.languages.getDiagnostics();
    }

    const result = diagnostics.flatMap(([uri, diags]) =>
        diags.map(d => ({
            file: vscode.workspace.asRelativePath(uri),
            line: d.range.start.line + 1,
            severity: vscode.DiagnosticSeverity[d.severity],
            message: d.message,
            source: d.source
        }))
    );

    return JSON.stringify(result);
}

async function applyEdit(params: any): Promise<string> {
    const uri = resolveUri(params.filePath);
    const doc = await vscode.workspace.openTextDocument(uri);
    const startLine = (params.startLine ?? 1) - 1;
    const endLine = params.endLine ?? doc.lineCount;
    const range = new vscode.Range(startLine, 0, endLine, 0);

    const edit = new vscode.WorkspaceEdit();
    edit.replace(uri, range, params.newText);
    const success = await vscode.workspace.applyEdit(edit);
    await doc.save();

    return success ? 'Edit applied successfully.' : 'Edit failed.';
}

async function createFile(params: any): Promise<string> {
    const uri = resolveUri(params.filePath);
    const content = params.content ?? '';
    await vscode.workspace.fs.writeFile(uri, Buffer.from(content, 'utf8'));
    return `File created: ${params.filePath}`;
}

async function deleteFile(params: any): Promise<string> {
    const uri = resolveUri(params.filePath);
    await vscode.workspace.fs.delete(uri);
    return `File deleted: ${params.filePath}`;
}

async function showDiff(params: any): Promise<string> {
    const uri = resolveUri(params.filePath);
    const original = await vscode.workspace.openTextDocument(uri);
    const proposedUri = vscode.Uri.parse(`untitled:${params.filePath}.proposed`);
    const proposed = await vscode.workspace.openTextDocument(
        { content: params.proposedContent, language: original.languageId });
    const title = params.diffTitle ?? `SharpClaw: ${params.filePath}`;
    await vscode.commands.executeCommand('vscode.diff', uri, proposed.uri, title);
    return `Diff shown for ${params.filePath}`;
}

async function runBuild(): Promise<string> {
    return new Promise((resolve) => {
        const task = new vscode.Task(
            { type: 'shell' },
            vscode.TaskScope.Workspace,
            'SharpClaw Build',
            'sharpclaw',
            new vscode.ShellExecution('dotnet build')
        );

        const disposable = vscode.tasks.onDidEndTaskProcess(e => {
            if (e.execution.task === task) {
                disposable.dispose();
                resolve(e.exitCode === 0
                    ? 'Build succeeded.'
                    : `Build failed with exit code ${e.exitCode}.`);
            }
        });

        vscode.tasks.executeTask(task);

        // Timeout after 120s
        setTimeout(() => {
            disposable.dispose();
            resolve('Build timed out after 120 seconds.');
        }, 120_000);
    });
}

async function runTerminal(params: any): Promise<string> {
    return new Promise((resolve) => {
        const cwd = params.workingDirectory
            ? resolveUri(params.workingDirectory).fsPath
            : vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;

        const task = new vscode.Task(
            { type: 'shell' },
            vscode.TaskScope.Workspace,
            'SharpClaw Terminal',
            'sharpclaw',
            new vscode.ShellExecution(params.command, { cwd })
        );

        const disposable = vscode.tasks.onDidEndTaskProcess(e => {
            if (e.execution.task === task) {
                disposable.dispose();
                resolve(`Command exited with code ${e.exitCode}.`);
            }
        });

        vscode.tasks.executeTask(task);

        setTimeout(() => {
            disposable.dispose();
            resolve('Command timed out after 60 seconds.');
        }, 60_000);
    });
}

// ═══════════════════════════════════════════════════════════════
// Helpers
// ═══════════════════════════════════════════════════════════════

function resolveUri(relativePath: string): vscode.Uri {
    const root = vscode.workspace.workspaceFolders?.[0]?.uri;
    if (!root) throw new Error('No workspace folder open.');
    return vscode.Uri.joinPath(root, relativePath);
}
