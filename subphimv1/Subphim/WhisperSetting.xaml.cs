using System.Text;
using System.Windows;
using System.Windows.Input; 

namespace subphimv1
{
    public partial class WhisperAdvancedSettingsWindow : Window
    {
        public WhisperAdvancedSettingsWindow()
        {
            InitializeComponent();
            this.Owner = Application.Current.MainWindow;
            PopulateArguments();
        }

        // *** THÊM PHƯƠNG THỨC NÀY VÀO ***
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void PopulateArguments()
        {
            var cppArgs = new StringBuilder();
            cppArgs.AppendLine("--help                   show this help message and exit");
            cppArgs.AppendLine("-h, --help");
            cppArgs.AppendLine("-t N, --threads N        number of threads to use during computation (default: 8)");
            cppArgs.AppendLine("-p N, --processors N     number of processors to use during computation (default: 1)");
            cppArgs.AppendLine("-ot N, --offset-t N      time offset in milliseconds (default: 0)");
            cppArgs.AppendLine("-os N, --offset-n N      segment index offset (default: 0)");
            cppArgs.AppendLine("-d N, --duration N       duration of audio to process in milliseconds (default: 0)");
            cppArgs.AppendLine("-mc N, --max-context N   maximum number of text context tokens to store (default: 224)");
            cppArgs.AppendLine("-ml N, --max-len N       maximum segment length in characters (default: 0)");
            cppArgs.AppendLine("-sow, --split-on-word    split on word rather than on token (default: false)");
            cppArgs.AppendLine("-bo N, --best-of N       number of best candidates to generate (default: 5)");
            cppArgs.AppendLine("-bs N, --beam-size N     beam size for beam search (default: 5)");
            cppArgs.AppendLine("-wt N, --word-thold N    word timestamp probability threshold (default: 0.01)");
            cppArgs.AppendLine("-et N, --entropy-thold N entropy threshold for decoder fail (default: 2.40)");
            cppArgs.AppendLine("-lpt N, --logprob-thold N  log probability threshold for decoder fail (default: -1.00)");
            cppArgs.AppendLine("-su, --speed-up          speed up audio by x2 (reduced accuracy)");
            cppArgs.AppendLine("-tr, --translate         translate from source language to english");
            cppArgs.AppendLine("-di, --diarize           enable speaker diarization");
            cppArgs.AppendLine("-nf, --no-fallback       do not fall back to simple transcription if temperature is high");
            cppArgs.AppendLine("-ps, --print-special     print special tokens");
            cppArgs.AppendLine("-pc, --print-colors      print colors");
            cppArgs.AppendLine("-pp, --print-progress    print progress");
            cppArgs.AppendLine("-nt, --no-timestamps     do not print timestamps");
            cppArgs.AppendLine("-l LANG, --language LANG spoken language ('auto' for auto-detect)");
            cppArgs.AppendLine("-m FNAME, --model FNAME  model path");
            cppArgs.AppendLine("-f FNAME, --file FNAME   path to audio file to transcribe");
            cppArgs.AppendLine("-oved D, --ov-e-device D the OpenVINO device used for encode inference");
            CppArgsTextBox.Text = cppArgs.ToString();

            var ctranslateArgs = new StringBuilder();
            ctranslateArgs.AppendLine("--model <MODEL_NAME_OR_PATH>");
            ctranslateArgs.AppendLine("--task <transcribe|translate>");
            ctranslateArgs.AppendLine("--language <LANGUAGE_CODE>");
            ctranslateArgs.AppendLine("--device <cpu|cuda>");
            ctranslateArgs.AppendLine("--device_index <0,1,2...>");
            ctranslateArgs.AppendLine("--compute_type <int8|int8_float16|int16|float16|float32>");
            ctranslateArgs.AppendLine("--threads <NUMBER>");
            ctranslateArgs.AppendLine("--output_dir <PATH>");
            ctranslateArgs.AppendLine("--output_format <all|srt|vtt|txt|tsv|json>");
            ctranslateArgs.AppendLine("--beam_size <NUMBER>");
            ctranslateArgs.AppendLine("--patience <NUMBER>");
            ctranslateArgs.AppendLine("--word_timestamps <True|False>");
            ctranslateArgs.AppendLine("--highlight_words <True|False>");
            ctranslateArgs.AppendLine("--max_line_width <NUMBER>");
            ctranslateArgs.AppendLine("--max_line_count <NUMBER>");
            ctranslateArgs.AppendLine("--initial_prompt <PROMPT>");
            CTranslate2ArgsTextBox.Text = ctranslateArgs.ToString();
        }
    }
}