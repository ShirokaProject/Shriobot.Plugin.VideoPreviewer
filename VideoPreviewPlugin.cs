using ShiroBot.Model.Common;
using ShiroBot.Model.Message.Requests;
using ShiroBot.Model.Message.Responses;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.Plugin.VideoPreviewer;

[BotPlugin(id:"VideoPreviewer",
        Description = "视频预览插件",
        Author = "greepar",
        Category = PluginCategory.Utility,
        Version = "1.0.0",
        GithubRepo = "ShirokaProject/Shriobot.Plugin.VideoPreviewer",
        IsPluginSingleFile = true)]
public sealed class VideoPreviewPlugin : PluginBase
{
    public override string Name => "VideoPreviewPlugin";
    
    private static readonly HashSet<string> SupportedVideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".flv", ".webm"
    };

    protected override Task LoadAsync()
    {
        FriendCommands.MapAll(async message =>
        {
            var targetSegment = message.Segments.FirstOrDefault(s => s is FileIncomingSegment or VideoIncomingSegment);
        
            switch (targetSegment)
            {
                case VideoIncomingSegment videoSegment:
                {
                    var videoUrl = videoSegment.TempUrl;
                    if (string.IsNullOrWhiteSpace(videoSegment.TempUrl)) return;
                    var previewUrl =  $"https://pv.qwq.lu/?url={videoUrl}";
                    BotLog.Info($"生成视频预览链接: {previewUrl}");
                    var response = await Context.Message.ReplyAsync(message, $"视频预览链接: {previewUrl}");
                    _ = Task.Run(() => ScheduleRecallMessageAsync(message.SenderId, response));
                    break;
                }
                case FileIncomingSegment fileSegment:
                {
                    //check if is video
                    var fileExtension = Path.GetExtension(fileSegment.FileName);
                    if (!SupportedVideoExtensions.Contains(fileExtension) || string.IsNullOrEmpty(fileSegment.FileHash)) return;
                    var getResponse =
                        await Context.File.GetPrivateFileDownloadUrlAsync(message.SenderId, fileSegment.FileId,fileSegment.FileHash);
                    var previewUrl = $"https://pv.qwq.lu/?url={Uri.EscapeDataString(getResponse.DownloadUrl)}";
                    BotLog.Info($"生成视频预览链接: {previewUrl}");
                    var response = await Context.Message.ReplyAsync(message, $"视频预览链接: {previewUrl}");
                    _ = Task.Run(() => ScheduleRecallMessageAsync(message.SenderId, response));
                    break;
                }
            }
        });
        
        GroupCommands.MapExact("v", async message =>
        {
            var replyIncomingSegment = message.Segments.OfType<ReplyIncomingSegment>().FirstOrDefault();
            if (replyIncomingSegment is null) return;
            
            var targetReplyContentSegment =
                replyIncomingSegment.Segments.FirstOrDefault(s => s is FileIncomingSegment or VideoIncomingSegment);
            
            if (targetReplyContentSegment is null &&
                replyIncomingSegment.Segments.Any(s => s is TextIncomingSegment { Text: "[文件]" or "[视频]" }))
            {
                // 手动获取被回复消息的内容
                BotLog.Info("无法直接获取被回复消息的内容，正在尝试手动获取...");
                var getReplyContentResponse = await Context.Message.GetMessageAsync(GetMessageRequestMessageScene.Group, message.Group.GroupId, replyIncomingSegment.MessageSeq);
                if (getReplyContentResponse.Message is not GroupIncomingMessage replyMessage) return;
                var replyContentSegment = replyMessage.Segments.FirstOrDefault(s => s is FileIncomingSegment or VideoIncomingSegment);
                if (replyContentSegment is null) return;
                targetReplyContentSegment = replyContentSegment;
            }
            
            switch (targetReplyContentSegment)
            {
                case FileIncomingSegment fileSegment:
                {
                    var fileExtension = Path.GetExtension(fileSegment.FileName);
                    if (!SupportedVideoExtensions.Contains(fileExtension)) return;
                    var getResponse = await Context.File.GetGroupFileDownloadUrlAsync(message.Group.GroupId,fileSegment.FileId);
                    var previewUrl = $"https://pv.qwq.lu/?url={Uri.EscapeDataString(getResponse.DownloadUrl)}";
                    BotLog.Info($"生成视频预览链接: {previewUrl}");
                    var response =  await Context.Message.ReplyAsync(message, $"视频预览链接: {previewUrl}\n(链接将在2min内撤回)");
                    _ = Task.Run(() => ScheduleRecallMessageAsync(message.Group.GroupId, response));
                    break;
                }
                case VideoIncomingSegment videoSegment:
                {
                    var fileUrl = videoSegment.TempUrl;
                    if (string.IsNullOrWhiteSpace(videoSegment.TempUrl))
                    { 
                        var getResponse = await Context.Message.GetResourceTempUrlAsync(videoSegment.ResourceId);
                        fileUrl = getResponse.Url;
                    }
                    var previewUrl = $"https://pv.qwq.lu/?url={Uri.EscapeDataString(fileUrl)}";
                    BotLog.Info($"生成视频预览链接: {previewUrl}");
                    var response =  await Context.Message.ReplyAsync(message, $"视频预览链接: {previewUrl}\n(链接将在2min内撤回)");
                    _ = Task.Run(() => ScheduleRecallMessageAsync(message.Group.GroupId, response));
                    break;
                }
                default:
                    return;
            }
        });
        return Task.CompletedTask;
    }
    
    //private method
    private async Task ScheduleRecallMessageAsync(long groupId,SendGroupMessageResponse response,int delaySeconds = 100)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
            BotLog.Info($"撤回群聊{groupId}的视频预览消息。");
            await Context.Message.RecallGroupMessageAsync(groupId, response.MessageSeq);
        }
        catch (Exception e)
        {
            foreach (var ownerId in Context.OwnerList)
            {
                await Context.Message.SendPrivateMessageAsync(ownerId, $"群聊{groupId}的消息撤回失败了: {e.Message}");
            }
            BotLog.Error($"群聊{groupId}的消息撤回失败了: {e}");
        }
    }
    
    private async Task ScheduleRecallMessageAsync(long userId,SendPrivateMessageResponse response,int delaySeconds = 100)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
            BotLog.Info($"撤回与用户{userId}的消息。");
            await Context.Message.RecallPrivateMessageAsync(userId, response.MessageSeq);
        }
        catch (Exception e)
        {
            foreach (var ownerId in Context.OwnerList)
            {
                await Context.Message.SendPrivateMessageAsync(ownerId, $"与用户{userId}的消息撤回失败了: {e.Message}");
            }
            BotLog.Error($"与用户{userId}的消息撤回失败了: {e}");
        }
    }
    
    protected override Task OnUnloadAsync()
    {
        return Task.CompletedTask;
    }
}
