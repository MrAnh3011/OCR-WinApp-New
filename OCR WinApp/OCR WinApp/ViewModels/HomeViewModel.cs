using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OCR.Business.Auth;
using OCR.Business.Processors;

namespace OCR_WinApp.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    public ObservableCollection<string> Features { get; } = new();
    public bool HasQuota { get; }
    public int QuotaTotal { get; }
    public int QuotaRead { get; }
    public int QuotaRemaining { get; }
    public bool HasQuotaRemaining { get; }
    public bool HasQuotaUsed { get; }
    public double QuotaRemainingPercent { get; }
    public double QuotaUsedPercent { get; }
    public string QuotaRemainingText { get; }
    public string QuotaUsedText { get; }
    public string QuotaSummaryText { get; }
    public string QuotaRemainingLegendText { get; }
    public string QuotaUsedLegendText { get; }

    public HomeViewModel(IEnumerable<IDocumentProcessor> processors, IAuthService authService)
    {
        foreach (var processor in processors)
            Features.Add(processor.Feature.DisplayName);

        var quota = authService.CurrentSession?.Quota;
        if (quota is null)
        {
            QuotaRemainingText = "0%";
            QuotaUsedText = "0%";
            QuotaSummaryText = "";
            QuotaRemainingLegendText = "";
            QuotaUsedLegendText = "";
            return;
        }

        QuotaTotal = Math.Max(0, quota.HanMuc);
        QuotaRead = Math.Max(0, quota.SoFileDaDoc);
        QuotaRemaining = Math.Max(0, quota.ConLai);
        HasQuotaRemaining = QuotaRemaining > 0;
        HasQuotaUsed = QuotaRead > 0;
        QuotaRemainingPercent = QuotaTotal > 0 ? QuotaRemaining * 100.0 / QuotaTotal : 0;
        QuotaUsedPercent = QuotaTotal > 0 ? QuotaRead * 100.0 / QuotaTotal : 0;
        HasQuota = QuotaTotal > 0;
        QuotaRemainingText = $"{QuotaRemainingPercent:0.#}%";
        QuotaUsedText = $"{QuotaUsedPercent:0.#}%";
        QuotaSummaryText = $"Tổng số: {QuotaTotal} file";
        QuotaRemainingLegendText = $"Còn lại: {QuotaRemaining} file ({QuotaRemainingPercent:0.#}%)";
        QuotaUsedLegendText = $"Đã dùng: {QuotaRead} file ({QuotaUsedPercent:0.#}%)";
    }
}
