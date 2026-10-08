using System.Text;
using System.Text.Json;
using CSharpFunctionalExtensions;
using OpenAI.Chat;
using SharedKernel;

namespace Shared.AI.OpenAiCompatible;

internal static class OpenAiCompatibleChatMessageFactory
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public static Result<List<ChatMessage>, Error> Build(
        AiGenerationRequest request,
        AiModelInfo modelInfo)
    {
        var textBuilder = new StringBuilder();
        var contentParts = new List<ChatMessageContentPart>();
        bool useContentParts = false;

        void AppendText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            if (!useContentParts)
            {
                if (textBuilder.Length > 0)
                    textBuilder.AppendLine().AppendLine();

                textBuilder.Append(text.Trim());
                return;
            }

            contentParts.Add(ChatMessageContentPart.CreateTextPart(text.Trim()));
        }

        void EnsureContentPartsMode()
        {
            if (useContentParts)
                return;

            useContentParts = true;

            if (textBuilder.Length == 0)
                return;

            contentParts.Add(ChatMessageContentPart.CreateTextPart(textBuilder.ToString().Trim()));
            textBuilder.Clear();
        }

        if (!modelInfo.SupportsSystemMessage)
            AppendText(request.SystemPrompt);

        AppendText(request.UserPrompt);

        if (request.Input is not null)
            AppendText($"Входные данные:\n{JsonSerializer.Serialize(request.Input, _jsonOptions)}");

        foreach (AiInputPart inputPart in request.InputParts)
        {
            switch (inputPart.Type)
            {
                case AiInputPartType.Text:
                    AppendText($"{inputPart.Name}:\n{inputPart.Text}");
                    break;

                case AiInputPartType.Audio:
                    if (inputPart.Content.IsEmpty)
                        return AiErrors.InputUnsupported("Для аудио-входа нужно передать содержимое файла");

                    EnsureContentPartsMode();
                    contentParts.Add(
                        ChatMessageContentPart.CreateInputAudioPart(
                            BinaryData.FromBytes(inputPart.Content.ToArray()),
                            ResolveAudioFormat(inputPart)));
                    break;

                default:
                    return AiErrors.InputUnsupported();
            }
        }

        if (textBuilder.Length == 0 && contentParts.Count == 0)
            return AiErrors.InputRequired();

        UserChatMessage userMessage = useContentParts
            ? new UserChatMessage(contentParts)
            : new UserChatMessage(textBuilder.ToString().Trim());

        List<ChatMessage> messages = [];

        if (modelInfo.SupportsSystemMessage && !string.IsNullOrWhiteSpace(request.SystemPrompt))
            messages.Add(new SystemChatMessage(request.SystemPrompt.Trim()));

        messages.Add(userMessage);

        return messages;
    }

    private static ChatInputAudioFormat ResolveAudioFormat(AiInputPart inputPart)
    {
        string format = !string.IsNullOrWhiteSpace(inputPart.ContentType)
            ? inputPart.ContentType.Trim().ToLowerInvariant()
            : Path.GetExtension(inputPart.FileName)?.TrimStart('.').ToLowerInvariant() ?? "wav";

        return format switch
        {
            "audio/wav" or "audio/x-wav" or "wav" => ChatInputAudioFormat.Wav,
            "audio/mp3" or "audio/mpeg" or "mp3" => ChatInputAudioFormat.Mp3,
            _ => ChatInputAudioFormat.Wav,
        };
    }
}
