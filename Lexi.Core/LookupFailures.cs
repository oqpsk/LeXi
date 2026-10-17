using System.ComponentModel;
using System.Text.Json;

namespace Lexi.Core;

public enum ModelAccessFailure { ClientMissing, SignInRequired, Unavailable }

// Carry only an application-defined category; never expose provider stderr or credentials.
public sealed class ModelAccessException(ModelAccessFailure failure) : InvalidOperationException(failure switch
{
    ModelAccessFailure.ClientMissing => "未找到 Codex。请安装官方客户端，或在设置中指定 Codex 程序路径。",
    ModelAccessFailure.SignInRequired => "Codex 登录已失效，请在 Codex 中重新登录。",
    _ => "Codex 暂未完成查询。请检查登录、网络、额度或模型名称。"
})
{
    public ModelAccessFailure Failure { get; } = failure;
}

public enum MergeFailureKind { Timeout, InvalidResponse, SignInRequired, ClientUnavailable, ServiceUnavailable, LocalAccess }

public static class MergeFailures
{
    public static MergeFailureKind Classify(Exception error) => error switch
    {
        TimeoutException or OperationCanceledException => MergeFailureKind.Timeout,
        InvalidDataException or JsonException => MergeFailureKind.InvalidResponse,
        ModelAccessException { Failure: ModelAccessFailure.SignInRequired } => MergeFailureKind.SignInRequired,
        ModelAccessException { Failure: ModelAccessFailure.ClientMissing } or Win32Exception => MergeFailureKind.ClientUnavailable,
        IOException or UnauthorizedAccessException => MergeFailureKind.LocalAccess,
        _ => MergeFailureKind.ServiceUnavailable
    };

    public static string Describe(MergeFailureKind? failure) => failure switch
    {
        MergeFailureKind.Timeout => "整理模型响应超时",
        MergeFailureKind.InvalidResponse => "整理结果格式不完整或未通过校验",
        MergeFailureKind.SignInRequired => "Codex 登录已失效，请到设置中重新登录",
        MergeFailureKind.ClientUnavailable => "无法启动 Codex，请检查设置中的客户端路径",
        MergeFailureKind.ServiceUnavailable => "整理模型调用失败，请检查登录、网络、额度或模型设置",
        MergeFailureKind.LocalAccess => "无法读取整理结果或访问所需的本地文件",
        _ => "旧结果未记录具体失败原因"
    };
}
