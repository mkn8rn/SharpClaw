# SharpClaw CLI

```
sharpclaw help
```

## Auth

```
sharpclaw register <username> <password>
sharpclaw login <username> <password>
sharpclaw login <username> <password> --remember
```

## Providers

```
sharpclaw provider add <name> [endpoint]
sharpclaw provider list
```

## Models

```
sharpclaw model add <name> <providerId>
sharpclaw model list
```

## Agents

```
sharpclaw agent add <name> <modelId> [system prompt]
sharpclaw agent list
```

## Chat

```
sharpclaw chat <agentId> <message>
```

## Server

Running with no arguments starts the localhost API:

```
sharpclaw
```
