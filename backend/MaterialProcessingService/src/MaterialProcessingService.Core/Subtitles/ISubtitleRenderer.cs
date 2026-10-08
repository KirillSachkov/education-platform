using MaterialProcessingService.Core.Transcripts;

namespace MaterialProcessingService.Core.Subtitles;

public interface ISubtitleRenderer
{
    string RenderSrt(Transcript transcript);
}
