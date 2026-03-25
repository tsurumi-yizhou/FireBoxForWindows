using System.Runtime.InteropServices;
using Core.Com.Structs;

namespace Core.Models;

public static class Mappers
{
    private static string BstrToString(IntPtr bstr) =>
        bstr == IntPtr.Zero ? string.Empty : Marshal.PtrToStringBSTR(bstr) ?? string.Empty;

    private static string? BstrToStringNullable(IntPtr bstr) =>
        bstr == IntPtr.Zero ? null : Marshal.PtrToStringBSTR(bstr);

    private static IntPtr StringToBstr(string? value) =>
        value is null ? IntPtr.Zero : Marshal.StringToBSTR(value);

    public static Result<ChatCompletionResponse> ToModel(in ChatCompletionResultStruct s)
    {
        if (s.HasResponse != 0)
        {
            var response = new ChatCompletionResponse(
                BstrToString(s.ModelId),
                new ChatMessage(BstrToString(s.MessageRole), BstrToString(s.MessageContent)),
                BstrToStringNullable(s.ReasoningText),
                new Usage(s.UsagePromptTokens, s.UsageCompletionTokens, s.UsageTotalTokens),
                BstrToString(s.FinishReason));
            return new Result<ChatCompletionResponse>(response, null);
        }

        return new Result<ChatCompletionResponse>(null, BstrToString(s.ErrorMessage));
    }

    public static Result<EmbeddingResponse> ToModel(in EmbeddingResultStruct s)
    {
        if (s.HasResponse != 0)
        {
            var indices = NativeArrayMarshaller.ReadIntArray(s.IndicesArray);
            var flatVectors = NativeArrayMarshaller.ReadFloatArray(s.FlatVectorsArray);
            var embeddings = new List<Embedding>(s.EmbeddingCount);

            if (s.VectorDimension > 0)
            {
                for (var i = 0; i < s.EmbeddingCount && i < indices.Length; i++)
                {
                    var vector = new float[s.VectorDimension];
                    Array.Copy(flatVectors, i * s.VectorDimension, vector, 0, s.VectorDimension);
                    embeddings.Add(new Embedding(indices[i], vector));
                }
            }

            var response = new EmbeddingResponse(
                BstrToString(s.ModelId),
                embeddings,
                new Usage(s.UsagePromptTokens, s.UsageCompletionTokens, s.UsageTotalTokens));
            return new Result<EmbeddingResponse>(response, null);
        }

        return new Result<EmbeddingResponse>(null, BstrToString(s.ErrorMessage));
    }

    public static Result<FunctionCallResponse> ToModel(in FunctionCallResultStruct s)
    {
        if (s.HasResponse != 0)
        {
            var response = new FunctionCallResponse(
                BstrToString(s.ModelId),
                BstrToString(s.OutputJson),
                new Usage(s.UsagePromptTokens, s.UsageCompletionTokens, s.UsageTotalTokens),
                BstrToString(s.FinishReason));
            return new Result<FunctionCallResponse>(response, null);
        }

        return new Result<FunctionCallResponse>(null, BstrToString(s.ErrorMessage));
    }

    public static ChatCompletionResultStruct ToStruct(Result<ChatCompletionResponse> result)
    {
        var s = new ChatCompletionResultStruct();
        if (result.Response is { } response)
        {
            s.HasResponse = -1;
            s.ModelId = StringToBstr(response.ModelId);
            s.MessageRole = StringToBstr(response.Message.Role);
            s.MessageContent = StringToBstr(response.Message.Content);
            s.ReasoningText = StringToBstr(response.ReasoningText);
            s.UsagePromptTokens = response.Usage.PromptTokens;
            s.UsageCompletionTokens = response.Usage.CompletionTokens;
            s.UsageTotalTokens = response.Usage.TotalTokens;
            s.FinishReason = StringToBstr(response.FinishReason);
        }
        else
        {
            s.HasResponse = 0;
            s.ErrorMessage = StringToBstr(result.Error);
        }

        return s;
    }

    public static EmbeddingResultStruct ToStruct(Result<EmbeddingResponse> result)
    {
        var s = new EmbeddingResultStruct();
        if (result.Response is { } response)
        {
            s.HasResponse = -1;
            s.ModelId = StringToBstr(response.ModelId);
            s.UsagePromptTokens = response.Usage.PromptTokens;
            s.UsageCompletionTokens = response.Usage.CompletionTokens;
            s.UsageTotalTokens = response.Usage.TotalTokens;
            s.EmbeddingCount = response.Embeddings.Count;
            s.VectorDimension = response.Embeddings.Count == 0 ? 0 : response.Embeddings[0].Vector.Length;

            if (response.Embeddings.Count > 0)
            {
                s.IndicesArray = NativeArrayMarshaller.CreateIntArray(response.Embeddings.Select(static embedding => embedding.Index).ToArray());

                var flatVectors = new List<float>(response.Embeddings.Count * s.VectorDimension);
                foreach (var embedding in response.Embeddings)
                    flatVectors.AddRange(embedding.Vector);
                s.FlatVectorsArray = NativeArrayMarshaller.CreateFloatArray(flatVectors);
            }
        }
        else
        {
            s.HasResponse = 0;
            s.ErrorMessage = StringToBstr(result.Error);
        }

        return s;
    }

    public static FunctionCallResultStruct ToStruct(Result<FunctionCallResponse> result)
    {
        var s = new FunctionCallResultStruct();
        if (result.Response is { } response)
        {
            s.HasResponse = -1;
            s.ModelId = StringToBstr(response.ModelId);
            s.OutputJson = StringToBstr(response.OutputJson);
            s.UsagePromptTokens = response.Usage.PromptTokens;
            s.UsageCompletionTokens = response.Usage.CompletionTokens;
            s.UsageTotalTokens = response.Usage.TotalTokens;
            s.FinishReason = StringToBstr(response.FinishReason);
        }
        else
        {
            s.HasResponse = 0;
            s.ErrorMessage = StringToBstr(result.Error);
        }

        return s;
    }

    public static void FreeBstrFields(in ChatCompletionResultStruct s)
    {
        Marshal.FreeBSTR(s.ModelId);
        Marshal.FreeBSTR(s.MessageRole);
        Marshal.FreeBSTR(s.MessageContent);
        Marshal.FreeBSTR(s.ReasoningText);
        Marshal.FreeBSTR(s.FinishReason);
        Marshal.FreeBSTR(s.ErrorMessage);
    }

    public static void FreeBstrFields(in EmbeddingResultStruct s)
    {
        Marshal.FreeBSTR(s.ModelId);
        Marshal.FreeBSTR(s.ErrorMessage);
    }

    public static void FreeArraysAndBstrFields(in EmbeddingResultStruct s)
    {
        NativeArrayMarshaller.DestroyArray(s.IndicesArray);
        NativeArrayMarshaller.DestroyArray(s.FlatVectorsArray);
        FreeBstrFields(in s);
    }

    public static void FreeBstrFields(in FunctionCallResultStruct s)
    {
        Marshal.FreeBSTR(s.ModelId);
        Marshal.FreeBSTR(s.OutputJson);
        Marshal.FreeBSTR(s.FinishReason);
        Marshal.FreeBSTR(s.ErrorMessage);
    }
}
