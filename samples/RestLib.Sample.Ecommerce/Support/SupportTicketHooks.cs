using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using RestLib;
using RestLib.Hooks;
using RestLib.Sample.Ecommerce.Identity;
using RestLib.Sample.Ecommerce.Models;

namespace RestLib.Sample.Ecommerce.Support;

/// <summary>
/// Named hooks for support ticket resources.
/// </summary>
public static class SupportTicketHooks
{
    /// <summary>
    /// The named hook used to stamp new support tickets with the authenticated requester.
    /// </summary>
    public const string PrepareSupportTicketHookName = "PrepareSupportTicket";

    private const string OpenStatus = "OPEN";

    /// <summary>
    /// Prepares a new support ticket before validation and persistence.
    /// </summary>
    /// <param name="context">The RestLib hook context.</param>
    /// <returns>A task that completes when the hook work is done.</returns>
    public static Task PrepareSupportTicketAsync(HookContext<SupportTicket, Guid> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Operation != RestLibOperation.Create || context.Entity is not { } ticket)
        {
            return Task.CompletedTask;
        }

        var currentUser = context.Services.GetRequiredService<ICurrentUser>();
        if (currentUser is not { UserId: { } userId } || (!currentUser.IsCustomer && !currentUser.IsCarrier))
        {
            Stop(context, Results.Forbid());
            return Task.CompletedTask;
        }

        if (ticket.Id == Guid.Empty)
        {
            ticket.Id = Guid.NewGuid();
        }

        ticket.CreatedByUserId = userId;
        ticket.CreatedByUser = null;
        ticket.Subject = NormalizeText(ticket.Subject);
        ticket.Message = NormalizeText(ticket.Message);
        ticket.Status = OpenStatus;
        ticket.CreatedAt = DateTime.UtcNow;

        return Task.CompletedTask;
    }

    private static string NormalizeText(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    private static void Stop(HookContext<SupportTicket, Guid> context, IResult result)
    {
        context.ShouldContinue = false;
        context.EarlyResult = result;
    }
}
