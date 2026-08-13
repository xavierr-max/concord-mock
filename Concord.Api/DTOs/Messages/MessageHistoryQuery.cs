using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Concord.Api.DTOs.Messages;

public sealed class MessageHistoryQuery
{
    [BindRequired, Range(1, int.MaxValue)]
    public int Page { get; init; }

    [BindRequired, Range(1, 100)]
    public int PageSize { get; init; }
}
