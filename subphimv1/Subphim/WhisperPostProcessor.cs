using subphimv1.Subphim;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace subphimv1.Services
{
    public class WhisperPostProcessor
    {
        public class PostProcessingOptions
        {
            public bool AddPeriods { get; set; }
            public bool FixCasing { get; set; }
            public bool MergeLines { get; set; }
            public string LanguageCode { get; set; }
        }

        public ObservableCollection<SrtSubtitleLine> Process(List<SrtSubtitleLine> inputLines, PostProcessingOptions options)
        {
            var processedLines = new ObservableCollection<SrtSubtitleLine>(inputLines);

            if (options.MergeLines)
            {
                // Logic gộp dòng đơn giản: Nếu 2 dòng gần nhau và ngắn, gộp lại
                for (int i = processedLines.Count - 2; i >= 0; i--)
                {
                    var current = processedLines[i];
                    var next = processedLines[i + 1];

                    if (next.StartTime - current.EndTime < System.TimeSpan.FromMilliseconds(200) &&
                        (current.OriginalText.Length + next.OriginalText.Length) < 80)
                    {
                        current.OriginalText += " " + next.OriginalText;
                        current.EndTime = next.EndTime;
                        processedLines.RemoveAt(i + 1);
                    }
                }
            }

            if (options.FixCasing)
            {
                bool sentenceStart = true;
                foreach (var line in processedLines)
                {
                    if (!string.IsNullOrWhiteSpace(line.OriginalText))
                    {
                        var text = line.OriginalText.ToLower();
                        if (sentenceStart && char.IsLetter(text[0]))
                        {
                            text = char.ToUpper(text[0]) + text.Substring(1);
                        }
                        line.OriginalText = text;

                        sentenceStart = text.Trim().EndsWith(".") || text.Trim().EndsWith("?") || text.Trim().EndsWith("!");
                    }
                }
            }

            if (options.AddPeriods)
            {
                for (int i = 0; i < processedLines.Count - 1; i++)
                {
                    var current = processedLines[i];
                    var next = processedLines[i + 1];
                    var text = current.OriginalText.Trim();

                    if (!string.IsNullOrEmpty(text) && !text.EndsWith(".") && !text.EndsWith("?") && !text.EndsWith("!"))
                    {
                        if (next.StartTime - current.EndTime > System.TimeSpan.FromMilliseconds(700))
                        {
                            current.OriginalText += ".";
                        }
                    }
                }
            }

            SrtFileUtils.ReIndex(processedLines);
            return processedLines;
        }
    }
}