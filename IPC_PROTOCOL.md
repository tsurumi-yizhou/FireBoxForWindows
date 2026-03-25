# FireBox IPC 协议文档

基于当前 Windows COM 实现整理，目标是为 Android Binder 和 macOS XPC 迁移提供一个平台无关的协议说明。本文刻意剥离 BSTR、COM struct、`IntPtr`、`SAFEARRAY` 等传输细节，只保留服务划分、接口名、请求、答复、事件和已观察到的行为语义。

当前 COM 类型库版本为 `1.5`，对应源文件主要在 `Core/Com`、`Service/Dispatch`、`Client`。

## 1. 总体结构

协议可以抽象为 3 个面：

1. `ControlService`
   管理面。供桌面管理 UI 调用，用于服务生命周期、统计信息、Provider/Route 配置、连接列表、客户端授权列表。
2. `CapabilityService`
   能力面。供受信任客户端调用，用于模型发现、聊天补全、流式聊天、Embedding、函数调用。
3. `ChatStreamSink`
   由客户端实现、服务端回调的单向事件流接口，仅用于流式聊天。

迁移到 Binder/XPC 时，建议继续保留这 3 个逻辑面，但不必保留 COM 的类、接口 UUID、数组 marshaller、BSTR 编码方式。

## 2. 通用约定

### 2.1 基本语义

- `requestId`: 64 位整数。用于标识一次流式聊天请求。
- 同步 AI 接口统一使用“结果联合体”语义：
  - 成功时返回 `response`
  - 失败时返回 `error`
  - 二者只能存在一个
- 流式聊天使用“起始应答 + 事件流 + 终止事件”的模式：
  - `StartChatCompletionStream` 先返回 `requestId`
  - 后续通过 `ChatStreamSink` 下发事件
  - 终止事件只能是 `Completed`、`Error`、`Cancelled` 三者之一

### 2.2 认证与授权

- `CapabilityService` 的所有方法都要求调用方被授权。
- 当前 Windows 实现通过 out-of-proc COM/RPC 获取真实调用进程 PID，再结合进程名与可执行文件路径做 allowlist 判定。
- 未授权调用通常在方法进入业务逻辑前被拒绝，因此很多情况下表现为“传输层调用失败”，而不是方法级 `error` 对象。

迁移建议：

- Android Binder 使用 UID/package/signing identity 作为调用方身份。
- macOS XPC 使用 audit token / code signing identity 作为调用方身份。
- 不要延续当前基于 `Ping("__firebox_identity__...")` 的辅助上报字符串，这只是 Windows 客户端包装层的附加行为，不应成为新协议的一部分。

### 2.3 枚举与常量

#### ProviderType

```text
OpenAI
Anthropic
Gemini
```

#### RouteStrategy

```text
Ordered
Random
```

#### ReasoningEffort

```text
Default = 0
Low     = 1
Medium  = 2
High    = 3
```

#### MediaFormat

```text
Image = 0
Video = 1
Audio = 2
```

#### MediaFormatMask

```text
ImageBit = 1
VideoBit = 2
AudioBit = 4
```

当前配置面持久化与部分能力发现接口使用 bitmask。迁移到 Binder/XPC 时，推荐对外协议优先使用数组枚举，只有在需要兼容旧存储格式时才保留 mask。

### 2.4 错误码

`FireBoxError.code` 的已定义取值如下：

| Code | Name | 含义 |
| --- | --- | --- |
| 1 | Security | 认证或授权失败 |
| 2 | InvalidArgument | 参数非法 |
| 3 | NoRoute | 找不到虚拟模型路由 |
| 4 | NoCandidate | 路由存在，但没有可用候选模型 |
| 5 | ProviderError | 上游模型提供商调用失败 |
| 6 | Timeout | 上游调用超时 |
| 7 | Internal | 服务内部错误 |
| 8 | Cancelled | 请求被取消 |

注意：当前 Windows 实现并没有稳定地覆盖所有错误码。尤其是同步 AI 接口上的前置失败，很多会被折叠为 `Internal`。迁移时建议把 `NoRoute`、`NoCandidate`、`Security`、`InvalidArgument` 真正落实为稳定的协议级错误。

## 3. 共享数据结构

以下使用 JSON 风格的伪 schema 表示逻辑结构，不代表必须真的走 JSON。

### 3.1 Usage

```json
{
  "promptTokens": 0,
  "completionTokens": 0,
  "totalTokens": 0
}
```

### 3.2 ProviderSelection

```json
{
  "providerId": 0,
  "providerType": "OpenAI",
  "providerName": "Primary OpenAI",
  "modelId": "gpt-4.1"
}
```

### 3.3 FireBoxError

```json
{
  "code": 5,
  "message": "Provider returned 429",
  "providerType": "OpenAI",
  "providerModelId": "gpt-4.1"
}
```

`providerType` 和 `providerModelId` 可为空；只有在错误能明确归因到某个上游 provider/model 时才带回。

### 3.4 ChatAttachment

平台无关的逻辑结构建议如下：

```json
{
  "mediaFormat": "Image",
  "mimeType": "image/png",
  "fileName": "diagram.png",
  "sizeBytes": 12345,
  "data": "<binary>"
}
```

当前 Windows COM 实现为了嵌在 JSON 里传输，使用的是 `base64Data` 字段，而不是二进制字段。迁移到 Binder/XPC 时可以直接传二进制。

### 3.5 ChatMessage

```json
{
  "role": "user",
  "content": "Explain this image",
  "attachments": [
    {
      "mediaFormat": "Image",
      "mimeType": "image/png",
      "fileName": "diagram.png",
      "sizeBytes": 12345,
      "data": "<binary>"
    }
  ]
}
```

### 3.6 ModelCapabilities

```json
{
  "reasoning": true,
  "toolCalling": false,
  "inputFormats": ["Image"],
  "outputFormats": []
}
```

### 3.7 VirtualModelSummary

```json
{
  "virtualModelId": "chat-default",
  "strategy": "Ordered",
  "capabilities": {
    "reasoning": true,
    "toolCalling": false,
    "inputFormats": ["Image"],
    "outputFormats": []
  },
  "available": true
}
```

### 3.8 ModelCandidateInfo

```json
{
  "providerId": 1,
  "providerType": "OpenAI",
  "providerName": "Main OpenAI",
  "baseUrl": "https://api.openai.com/v1/",
  "modelId": "gpt-4.1",
  "enabledInConfig": true,
  "capabilitySupported": true
}
```

### 3.9 ChatCompletionResponse

```json
{
  "virtualModelId": "chat-default",
  "message": {
    "role": "assistant",
    "content": "Hello"
  },
  "reasoningText": "optional internal reasoning text",
  "selection": {
    "providerId": 1,
    "providerType": "OpenAI",
    "providerName": "Main OpenAI",
    "modelId": "gpt-4.1"
  },
  "usage": {
    "promptTokens": 10,
    "completionTokens": 20,
    "totalTokens": 30
  },
  "finishReason": "stop"
}
```

### 3.10 ChatCompletionResult

```json
{
  "response": { "...": "ChatCompletionResponse" },
  "error": null
}
```

或

```json
{
  "response": null,
  "error": {
    "code": 5,
    "message": "Provider returned 429",
    "providerType": "OpenAI",
    "providerModelId": "gpt-4.1"
  }
}
```

### 3.11 Embedding

```json
{
  "index": 0,
  "vector": [0.1, -0.2, 0.3]
}
```

### 3.12 EmbeddingResponse

```json
{
  "virtualModelId": "embed-default",
  "embeddings": [
    {
      "index": 0,
      "vector": [0.1, -0.2, 0.3]
    }
  ],
  "selection": {
    "providerId": 2,
    "providerType": "OpenAI",
    "providerName": "Embedding OpenAI",
    "modelId": "text-embedding-3-large"
  },
  "usage": {
    "promptTokens": 20,
    "completionTokens": 0,
    "totalTokens": 20
  }
}
```

### 3.13 EmbeddingResult

```json
{
  "response": { "...": "EmbeddingResponse" },
  "error": null
}
```

### 3.14 FunctionCallResponse

```json
{
  "virtualModelId": "tool-default",
  "outputJson": "{\"answer\":42}",
  "selection": {
    "providerId": 1,
    "providerType": "OpenAI",
    "providerName": "Main OpenAI",
    "modelId": "gpt-4.1"
  },
  "usage": {
    "promptTokens": 10,
    "completionTokens": 40,
    "totalTokens": 50
  },
  "finishReason": "stop"
}
```

### 3.15 FunctionCallResult

```json
{
  "response": { "...": "FunctionCallResponse" },
  "error": null
}
```

## 4. ControlService

`ControlService` 是严格的 request/reply 风格，不包含事件流。

### 4.1 Ping

请求：

```json
{
  "message": "hello"
}
```

答复：

```json
{
  "result": "Pong: hello"
}
```

说明：当前仅作连通性探测。

### 4.2 Shutdown

请求：

```json
{}
```

答复：

```json
{}
```

说明：请求服务进程退出。

### 4.3 GetDailyStats

请求：

```json
{
  "year": 2026,
  "month": 3,
  "day": 25
}
```

答复：

```json
{
  "requestCount": 100,
  "promptTokens": 1000,
  "completionTokens": 2000,
  "totalTokens": 3000,
  "estimatedCostUsd": 12.34
}
```

### 4.4 GetMonthlyStats

请求：

```json
{
  "year": 2026,
  "month": 3
}
```

答复与 `GetDailyStats` 相同。

### 4.5 ListProviders

请求：

```json
{}
```

答复：

```json
{
  "providers": [
    {
      "id": 1,
      "providerType": "OpenAI",
      "name": "Main OpenAI",
      "baseUrl": "https://api.openai.com/v1/",
      "enabledModelIds": ["gpt-4.1", "gpt-4o-mini"],
      "createdAt": "2026-03-25T12:00:00Z",
      "updatedAt": "2026-03-25T12:30:00Z"
    }
  ]
}
```

说明：不返回 API Key。

### 4.6 AddProvider

请求：

```json
{
  "providerType": "OpenAI",
  "name": "Main OpenAI",
  "baseUrl": "https://api.openai.com/v1/",
  "apiKey": "secret"
}
```

答复：

```json
{
  "providerId": 1
}
```

### 4.7 UpdateProvider

请求：

```json
{
  "providerId": 1,
  "name": "Main OpenAI",
  "baseUrl": "https://api.openai.com/v1/",
  "apiKey": "optional-new-secret-or-empty-string-to-keep",
  "enabledModelIds": ["gpt-4.1", "gpt-4o-mini"]
}
```

答复：

```json
{}
```

说明：

- 当前实现的 Windows 管理接口传的是 `enabledModelIdsJson` 字符串，但逻辑语义就是字符串数组。
- 空字符串表示“不更新该字段”，这是当前实现行为；新协议建议显式区分“未提供”和“提供空数组”。

### 4.8 DeleteProvider

请求：

```json
{
  "providerId": 1
}
```

答复：

```json
{}
```

### 4.9 FetchProviderModels

请求：

```json
{
  "providerId": 1
}
```

答复：

```json
{
  "modelIds": ["gpt-4.1", "gpt-4o-mini"]
}
```

### 4.10 ListRoutes

请求：

```json
{}
```

答复：

```json
{
  "routes": [
    {
      "id": 1,
      "virtualModelId": "chat-default",
      "strategy": "Ordered",
      "candidates": [
        {
          "providerId": 1,
          "modelId": "gpt-4.1"
        }
      ],
      "reasoning": true,
      "toolCalling": false,
      "inputFormatsMask": 1,
      "outputFormatsMask": 0,
      "createdAt": "2026-03-25T12:00:00Z",
      "updatedAt": "2026-03-25T12:30:00Z"
    }
  ]
}
```

### 4.11 AddRoute

请求：

```json
{
  "virtualModelId": "chat-default",
  "strategy": "Ordered",
  "candidates": [
    {
      "providerId": 1,
      "modelId": "gpt-4.1"
    }
  ],
  "reasoning": true,
  "toolCalling": false,
  "inputFormatsMask": 1,
  "outputFormatsMask": 0
}
```

答复：

```json
{
  "routeId": 1
}
```

### 4.12 UpdateRoute

请求：

```json
{
  "routeId": 1,
  "virtualModelId": "chat-default",
  "strategy": "Ordered",
  "candidates": [
    {
      "providerId": 1,
      "modelId": "gpt-4.1"
    }
  ],
  "reasoning": true,
  "toolCalling": false,
  "inputFormatsMask": 1,
  "outputFormatsMask": 0
}
```

答复：

```json
{}
```

### 4.13 DeleteRoute

请求：

```json
{
  "routeId": 1
}
```

答复：

```json
{}
```

### 4.14 ListConnections

请求：

```json
{}
```

答复：

```json
{
  "connections": [
    {
      "connectionId": 1,
      "processId": 1234,
      "processName": "SomeClient",
      "executablePath": "/path/to/client",
      "connectedAt": "2026-03-25T12:00:00Z",
      "requestCount": 42,
      "hasActiveStream": true
    }
  ]
}
```

说明：当前 Windows 管理 UI 没有消费 `hasActiveStream`，但服务端确实返回了该字段。

### 4.15 ListClientAccess

请求：

```json
{}
```

答复：

```json
{
  "records": [
    {
      "id": 1,
      "processId": 1234,
      "processName": "SomeClient",
      "executablePath": "/path/to/client",
      "requestCount": 42,
      "firstSeenAt": "2026-03-25T12:00:00Z",
      "lastSeenAt": "2026-03-25T13:00:00Z",
      "isAllowed": true,
      "deniedUntilUtc": null
    }
  ]
}
```

说明：当前 UI DTO 没有读取 `deniedUntilUtc`，但服务端序列化的是完整实体，字段存在。

### 4.16 UpdateClientAccessAllowed

请求：

```json
{
  "accessId": 1,
  "isAllowed": true
}
```

答复：

```json
{}
```

## 5. CapabilityService

### 5.1 Ping

请求：

```json
{
  "message": "hello"
}
```

答复：

```json
{
  "result": "Pong: hello"
}
```

说明：

- 当前 Windows 客户端连接后会额外发送 `__firebox_identity__:{pid}|{name}|{path}` 这种特殊消息。
- 这是客户端包装层的附加行为，不建议作为新平台协议的一部分。

### 5.2 ListVirtualModels

平台无关的推荐形式：

请求：

```json
{}
```

答复：

```json
{
  "models": [
    {
      "virtualModelId": "chat-default",
      "strategy": "Ordered",
      "capabilities": {
        "reasoning": true,
        "toolCalling": false,
        "inputFormats": ["Image"],
        "outputFormats": []
      },
      "available": true
    }
  ]
}
```

当前 Windows COM 实际上拆成了 3 个方法：

1. `GetVirtualModelCount()`
2. `GetVirtualModelAt(index)`
3. `ListVirtualModels()`

这属于 COM 传输优化痕迹。Binder/XPC 迁移建议只保留一个 `ListVirtualModels`。

### 5.3 GetModelCandidates

请求：

```json
{
  "virtualModelId": "chat-default"
}
```

答复：

```json
{
  "candidates": [
    {
      "providerId": 1,
      "providerType": "OpenAI",
      "providerName": "Main OpenAI",
      "baseUrl": "https://api.openai.com/v1/",
      "modelId": "gpt-4.1",
      "enabledInConfig": true,
      "capabilitySupported": true
    }
  ]
}
```

### 5.4 ChatCompletion

请求：

```json
{
  "virtualModelId": "chat-default",
  "messages": [
    {
      "role": "user",
      "content": "Hello",
      "attachments": []
    }
  ],
  "temperature": -1.0,
  "maxOutputTokens": -1,
  "reasoningEffort": "Default"
}
```

答复：`ChatCompletionResult`

```json
{
  "response": {
    "virtualModelId": "chat-default",
    "message": {
      "role": "assistant",
      "content": "Hi"
    },
    "reasoningText": null,
    "selection": {
      "providerId": 1,
      "providerType": "OpenAI",
      "providerName": "Main OpenAI",
      "modelId": "gpt-4.1"
    },
    "usage": {
      "promptTokens": 10,
      "completionTokens": 20,
      "totalTokens": 30
    },
    "finishReason": "stop"
  },
  "error": null
}
```

失败示例：

```json
{
  "response": null,
  "error": {
    "code": 7,
    "message": "No available candidates for 'chat-default'",
    "providerType": null,
    "providerModelId": null
  }
}
```

说明：

- 当前逻辑会先根据 `virtualModelId` 选择一个候选 provider/model，再调用上游网关。
- Provider 选择会考虑 route strategy、模型是否启用、能力是否支持、API key 是否可解密且非空。
- 当前同步实现对前置失败的错误分型不够精确，迁移后建议返回真正的 `NoRoute` 或 `NoCandidate`。

### 5.5 StartChatCompletionStream

请求：

```json
{
  "virtualModelId": "chat-default",
  "messages": [
    {
      "role": "user",
      "content": "Tell me a story",
      "attachments": []
    }
  ],
  "temperature": -1.0,
  "maxOutputTokens": -1,
  "reasoningEffort": "Default"
}
```

立即答复：

```json
{
  "requestId": 1001
}
```

后续事件通过 `ChatStreamSink` 下发，见第 6 节。

说明：

- 当前服务端在真正进入流式循环前就会生成 `requestId`。
- 如果在首个 token 前失败，客户端可能只收到 `Error`，而收不到 `Started`。

### 5.6 CancelChatCompletion

请求：

```json
{
  "requestId": 1001
}
```

答复：

```json
{}
```

说明：

- 取消是 best-effort。
- 正常情况下，对应流会收到终止事件 `Cancelled`。

### 5.7 CreateEmbeddings

平台无关的推荐形式：

请求：

```json
{
  "virtualModelId": "embed-default",
  "input": [
    "first text",
    "second text"
  ]
}
```

推荐答复：`EmbeddingResult`

```json
{
  "response": {
    "virtualModelId": "embed-default",
    "embeddings": [
      {
        "index": 0,
        "vector": [0.1, -0.2, 0.3]
      },
      {
        "index": 1,
        "vector": [0.4, -0.5, 0.6]
      }
    ],
    "selection": {
      "providerId": 1,
      "providerType": "OpenAI",
      "providerName": "Embedding OpenAI",
      "modelId": "text-embedding-3-large"
    },
    "usage": {
      "promptTokens": 20,
      "completionTokens": 0,
      "totalTokens": 20
    }
  },
  "error": null
}
```

当前 Windows COM 实际是两步：

1. `CreateEmbeddings` 返回元数据和 `embeddingRequestId`
2. `GetEmbeddingVectors(embeddingRequestId)` 再取向量数组

这样做只是为了规避 COM struct 里直接承载大体积 float 数组。Binder/XPC 迁移时，如果消息大小可控，建议合并成单次响应。

### 5.8 GetEmbeddingVectors

仅在需要兼容当前两步式设计时保留。

请求：

```json
{
  "embeddingRequestId": 2001
}
```

答复：

```json
{
  "vectorDimension": 1536,
  "embeddings": [
    {
      "index": 0,
      "vector": [0.1, -0.2, 0.3]
    },
    {
      "index": 1,
      "vector": [0.4, -0.5, 0.6]
    }
  ]
}
```

说明：

- 当前实现的 `embeddingRequestId` 是服务端缓存键。
- `GetEmbeddingVectors` 读取后会从缓存中移除，是一次性消费语义。
- 如果 `embeddingRequestId` 无效，当前实现返回空数组与 `vectorDimension = 0`，不会抛协议级错误。

### 5.9 CallFunction

请求：

```json
{
  "virtualModelId": "tool-default",
  "functionName": "summarize_invoice",
  "functionDescription": "Summarize invoice totals",
  "inputJson": "{\"lines\":[...]}",
  "inputSchemaJson": "{...}",
  "outputSchemaJson": "{...}",
  "temperature": 0.0,
  "maxOutputTokens": 1024
}
```

答复：`FunctionCallResult`

```json
{
  "response": {
    "virtualModelId": "tool-default",
    "outputJson": "{\"subtotal\":100,\"tax\":8}",
    "selection": {
      "providerId": 1,
      "providerType": "OpenAI",
      "providerName": "Main OpenAI",
      "modelId": "gpt-4.1"
    },
    "usage": {
      "promptTokens": 10,
      "completionTokens": 40,
      "totalTokens": 50
    },
    "finishReason": "stop"
  },
  "error": null
}
```

## 6. ChatStreamSink

`ChatStreamSink` 是由客户端实现、服务端调用的单向事件接口。

### 6.1 OnStarted

```json
{
  "requestId": 1001,
  "selection": {
    "providerId": 1,
    "providerType": "OpenAI",
    "providerName": "Main OpenAI",
    "modelId": "gpt-4.1"
  }
}
```

### 6.2 OnDelta

```json
{
  "requestId": 1001,
  "deltaText": "Hello"
}
```

### 6.3 OnReasoningDelta

```json
{
  "requestId": 1001,
  "reasoningText": "Need to inspect the image first"
}
```

### 6.4 OnUsage

```json
{
  "requestId": 1001,
  "usage": {
    "promptTokens": 10,
    "completionTokens": 20,
    "totalTokens": 30
  }
}
```

### 6.5 OnCompleted

```json
{
  "requestId": 1001,
  "messageRole": "assistant",
  "messageContent": "Final full answer",
  "reasoningText": "optional final reasoning text",
  "finishReason": "stop",
  "usage": {
    "promptTokens": 10,
    "completionTokens": 20,
    "totalTokens": 30
  }
}
```

说明：当前回调完成事件没有重复携带 `selection`，因为该信息已在 `OnStarted` 中给出。

### 6.6 OnError

```json
{
  "requestId": 1001,
  "error": {
    "code": 5,
    "message": "Provider returned 429",
    "providerType": "OpenAI",
    "providerModelId": "gpt-4.1"
  }
}
```

### 6.7 OnCancelled

```json
{
  "requestId": 1001
}
```

### 6.8 事件时序

典型成功时序：

```text
StartChatCompletionStream(request) -> { requestId }
OnStarted
OnDelta*
OnReasoningDelta*
OnUsage?
OnCompleted
```

失败时序：

```text
StartChatCompletionStream(request) -> { requestId }
[OnStarted?]
OnError
```

取消时序：

```text
StartChatCompletionStream(request) -> { requestId }
[OnStarted?]
CancelChatCompletion(requestId)
OnCancelled
```

约束：

- `OnStarted` 不是绝对保证事件，早期失败可以直接进入 `OnError`。
- `OnCompleted`、`OnError`、`OnCancelled` 都会结束该流。
- `OnUsage` 是否出现取决于上游 provider 是否在流式过程中提供 token 统计。

## 7. 当前实现行为与迁移建议

### 7.1 建议保留的逻辑能力

- `ControlService` / `CapabilityService` / `ChatStreamSink` 三分结构
- 虚拟模型路由 `virtualModelId`
- Provider 选择结果 `selection`
- 同步返回 `response|error` 联合体
- 流式回调事件模型
- 客户端 allowlist 控制面

### 7.2 建议清理的 COM 痕迹

1. 不保留 `GetVirtualModelCount + GetVirtualModelAt + ListVirtualModels` 这种拆分形式，统一成单个列表接口。
2. 不保留 `messagesJson` / `attachmentsJson` 双 JSON 参数，统一为真正的结构化消息数组。
3. 不保留 `ChatAttachment.base64Data`，改为原生二进制字段。
4. 如果传输层允许，合并 `CreateEmbeddings` 与 `GetEmbeddingVectors`。
5. 不再使用 `Ping("__firebox_identity__...")` 这种魔法字符串承载身份信息。

### 7.3 建议修正的行为差异

1. 同步 `ChatCompletion` / `CreateEmbeddings` / `CallFunction` 应稳定返回 `NoRoute`、`NoCandidate`、`InvalidArgument` 等精确错误，而不是把前置失败折叠成 `Internal`。
2. 对 `GetEmbeddingVectors` 这类无效 ID 请求，建议返回明确错误而不是静默空结果。
3. `UpdateProvider` 建议显式区分“字段未提供”和“字段提供为空值”。
4. `ListVirtualModels` 可以考虑直接附带 `candidates`，避免调用方再做第二次查询；如果担心体积，可维持拆分，但那应是明确设计，不应只是 COM 限制遗留。

### 7.4 Android Binder / macOS XPC 映射建议

- Binder
  - `ControlService` / `CapabilityService` 映射为两个 AIDL 接口，或一个 AIDL 接口中的两个 service domain。
  - `ChatStreamSink` 映射为客户端传入的回调 Binder 接口。
  - 调用方身份来自 Binder UID，再结合包名和签名校验。
- XPC
  - `ControlService` / `CapabilityService` 可映射为两个 exported object，或一个 service 上的两个 selector group。
  - `ChatStreamSink` 映射为 reply block + incremental event channel，或单独的 callback proxy。
  - 调用方身份来自 audit token / code signature。

## 8. 建议作为迁移版协议的最小接口集

如果目标是“保留能力，去掉 COM 杂质”，建议最终跨平台协议最少保留以下接口：

### ControlService

- `Ping`
- `Shutdown`
- `GetDailyStats`
- `GetMonthlyStats`
- `ListProviders`
- `AddProvider`
- `UpdateProvider`
- `DeleteProvider`
- `FetchProviderModels`
- `ListRoutes`
- `AddRoute`
- `UpdateRoute`
- `DeleteRoute`
- `ListConnections`
- `ListClientAccess`
- `UpdateClientAccessAllowed`

### CapabilityService

- `Ping`
- `ListVirtualModels`
- `GetModelCandidates`
- `ChatCompletion`
- `StartChatCompletionStream`
- `CancelChatCompletion`
- `CreateEmbeddings`
- `CallFunction`

### ChatStreamSink

- `OnStarted`
- `OnDelta`
- `OnReasoningDelta`
- `OnUsage`
- `OnCompleted`
- `OnError`
- `OnCancelled`

如果消息体尺寸允许，`GetEmbeddingVectors` 可以在新协议中移除。