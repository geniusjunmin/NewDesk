using System;
using System.Linq;
using System.Threading.Tasks;
using NewDesk.Models.Ai;

namespace NewDesk.Services.Ai;

public static class DailyBriefService
{
    private static string? _lastBrief;
    private static DateTime? _lastGeneratedDate;

    public static async Task<string> GetDailyBriefAsync(bool forceRefresh = false)
    {
        if (!forceRefresh && _lastGeneratedDate.HasValue && _lastGeneratedDate.Value.Date == DateTime.Today)
        {
            if (!string.IsNullOrEmpty(_lastBrief)) return _lastBrief;
        }

        try
        {
            string context = AiContextBroker.BuildContextPrompt(AiDataCategory.Reminder | AiDataCategory.Wallpaper).Prompt;
            string prompt = $"请根据以下桌面状态，生成一段不超过 100 字的今日晨间/日间高效生活与工作小结（语言亲切温暖）：\n{context}";

            var req = new AiTurnRequest
            {
                UserPrompt = prompt,
                TaskProfile = AiTaskProfile.GeneralChat,
                DataSensitivity = DataSensitivity.Personal,
                DataCategories = AiDataCategory.Reminder | AiDataCategory.Wallpaper
            };

            var resp = await AiOrchestrator.ExecuteTurnAsync(req);
            if (!string.IsNullOrEmpty(resp.Content))
            {
                _lastBrief = resp.Content.Trim();
                _lastGeneratedDate = DateTime.Today;
                return _lastBrief;
            }
        }
        catch (Exception ex)
        {
            AppDataPath.LogError("DailyBriefService.GetDailyBriefAsync", ex);
        }

        return _lastBrief ?? $"{DateTime.Now:yyyy-MM-dd dddd} · 保持专注，享受美好的一天！";
    }
}
