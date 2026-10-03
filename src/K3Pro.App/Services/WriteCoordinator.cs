using K3Pro.Protocol;
using static K3Pro.Protocol.Lang;

namespace K3Pro.App.Services;

public enum ApplyResult { Nothing, Applied, Failed }

/// <summary>
/// Shared Apply flow for every tab — always writes for real, no dialog, no dry-run (the user's request, 2026-10-02):
/// empty plan → report no changes; otherwise → <see cref="IDeviceService.ExecuteAsync"/>, then report saved / error.
/// Every packet sent / received still goes to <see cref="PacketLog"/>. Dry-run only exists in the CLI (default; real writes need --send).
/// </summary>
public sealed class WriteCoordinator(IDeviceService device, PacketLog log, Action<string, bool> notify)
{
    public async Task<ApplyResult> ApplyAsync(WritePlan plan)
    {
        foreach (var note in plan.Notes) log.Info(note);
        if (plan.IsEmpty)
        {
            notify(T($"Không có gì thay đổi ({plan.Title}).", $"Nothing changed ({plan.Title})."), false);
            return ApplyResult.Nothing;
        }

        var kind = device.State.Kind ?? ConnectionKind.Wired;
        if (WireEncoding.WhyUnsupported(kind, plan) is { } why)
        {
            log.Error(why);
            notify(why, true);
            return ApplyResult.Failed;
        }

        try
        {
            await device.ExecuteAsync(plan, kind);
            var saved = T($"✅ Đã lưu: {plan.Title}", $"✅ Saved: {plan.Title}");
            log.Info(saved);
            notify(saved, false);
            return ApplyResult.Applied;
        }
        catch (Exception ex)
        {
            log.Error(T($"Lưu thất bại ({plan.Title}): {ex.Message}", $"Save failed ({plan.Title}): {ex.Message}"));
            notify(T($"Lưu thất bại: {ex.Message}", $"Save failed: {ex.Message}"), true);
            return ApplyResult.Failed;
        }
    }
}
