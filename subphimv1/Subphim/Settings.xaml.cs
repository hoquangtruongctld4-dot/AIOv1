using Microsoft.Win32;
using Ookii.Dialogs.Wpf;
using subphimv1.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace subphimv1
{
    public partial class Settings : Window
    {
        public string GoogleDriveFolderId { get; set; }
        public string PathVSF { get; set; }
        public string OcrTextFolderName { get; set; }
        public string CmdVsfArgs { get; set; }
        public string CustomVsfOutputBaseDir { get; set; }
        public string CustomSubtitleOutputDir { get; set; }
        public VsfVideoOpenMethod SelectedVsfOpenMethod { get; set; }
        public VsfProcessingMode SelectedVsfProcessingMode { get; set; }
        public string VsfNumThreadsSearch { get; set; }
        public string VsfNumThreadsClean { get; set; }
        public bool UseCuda { get; set; }
        public bool DeleteRawTexts { get; set; }
        public bool DeleteTexts { get; set; }
        public bool CompressRawTexts { get; set; }
        public List<string> GeminiApiKeysOcr { get; set; } = new List<string>();
        public int GeminiImagesPerRequestOcr { get; set; }
        public int GeminiRequestsPerMinuteOcr { get; set; }
        public bool GeminiEnableMultiKeyOcr { get; set; }
        public string SelectedGeminiModelOcr { get; set; }
        public List<string> GeminiCustomModelsOcr { get; set; } = new List<string>();
        public string SelectedChatGPTModelSrt { get; set; }
        public List<string> ChatGPTCustomModelsSrt { get; set; } = new List<string>();
        public int ChatGPTSrtBatchSize { get; set; }

        // SRT Translation Settings
        public string ChutesApiKeySrt { get; set; }
        public List<string> GeminiApiKeysSrt { get; set; } = new List<string>();
        public bool GeminiEnableMultiKeySrt { get; set; }
        public SrtApiProvider SelectedSrtApiProvider { get; set; }
        public string SelectedChutesModelSrt { get; set; }
        public string SelectedGeminiModelSrt { get; set; }
        public List<string> ChutesCustomModelsSrt { get; set; } = new List<string>();
        public List<string> GeminiCustomModelsSrt { get; set; } = new List<string>();
        public int GeminiSrtTranslationRpm { get; set; }
        public int GeminiSrtTranslationBatchSize { get; set; }
        public int GeminiSrtThinkingBudget { get; set; }

        public Settings(
         string googleDriveFolderId, string pathVSF, string ocrTextFolderName, string cmdVsfArgs,
         string customVsfOutputBaseDir, string customSubtitleOutputDir, VsfVideoOpenMethod selectedVsfOpenMethod,
         VsfProcessingMode selectedVsfProcessingMode, string vsfNumThreadsSearch, string vsfNumThreadsClean,
         bool useCuda, bool deleteRawTexts, bool deleteTexts, bool compressRawTexts,
         List<string> geminiApiKeysOcr, int geminiImagesPerRequestOcr, int geminiRequestsPerMinuteOcr, bool geminiEnableMultiKeyOcr,
         string selectedGeminiModelOcr, List<string> geminiCustomModelsOcr,
         string chutesApiKeySrt, List<string> geminiApiKeysSrt, bool geminiEnableMultiKeySrt,
         SrtApiProvider selectedSrtApiProvider, string selectedChutesModelSrt, string selectedGeminiModelSrt,
         string selectedChatGPTModelSrt,
         List<string> chutesCustomModelsSrt, List<string> geminiCustomModelsSrt, List<string> chatGPTCustomModelsSrt,
         int geminiSrtTranslationRpm, int geminiSrtTranslationBatchSize, int geminiSrtThinkingBudget,
         int chatGptSrtBatchSize)
        {
            InitializeComponent();
            this.Owner = Application.Current.MainWindow;
            GoogleDriveFolderId = googleDriveFolderId;
            PathVSF = pathVSF;
            OcrTextFolderName = ocrTextFolderName;
            CmdVsfArgs = cmdVsfArgs;
            CustomVsfOutputBaseDir = customVsfOutputBaseDir;
            CustomSubtitleOutputDir = customSubtitleOutputDir;
            SelectedVsfOpenMethod = selectedVsfOpenMethod;
            SelectedVsfProcessingMode = selectedVsfProcessingMode;
            VsfNumThreadsSearch = vsfNumThreadsSearch;
            VsfNumThreadsClean = vsfNumThreadsClean;
            UseCuda = useCuda;
            DeleteRawTexts = deleteRawTexts;
            DeleteTexts = deleteTexts;
            CompressRawTexts = compressRawTexts;
            GeminiApiKeysOcr = geminiApiKeysOcr ?? new List<string>();
            GeminiImagesPerRequestOcr = geminiImagesPerRequestOcr;
            GeminiRequestsPerMinuteOcr = geminiRequestsPerMinuteOcr;
            GeminiEnableMultiKeyOcr = geminiEnableMultiKeyOcr;
            SelectedGeminiModelOcr = selectedGeminiModelOcr;
            GeminiCustomModelsOcr = geminiCustomModelsOcr ?? new List<string>();
            ChutesApiKeySrt = chutesApiKeySrt;
            GeminiApiKeysSrt = geminiApiKeysSrt ?? new List<string>();
            GeminiEnableMultiKeySrt = geminiEnableMultiKeySrt;
            SelectedSrtApiProvider = selectedSrtApiProvider;
            SelectedChutesModelSrt = selectedChutesModelSrt;
            SelectedGeminiModelSrt = selectedGeminiModelSrt;
            ChutesCustomModelsSrt = chutesCustomModelsSrt ?? new List<string>();
            GeminiCustomModelsSrt = geminiCustomModelsSrt ?? new List<string>();
            GeminiSrtTranslationRpm = geminiSrtTranslationRpm;
            GeminiSrtTranslationBatchSize = geminiSrtTranslationBatchSize;
            GeminiSrtThinkingBudget = geminiSrtThinkingBudget;
            ChutesCustomModelsSrt = chutesCustomModelsSrt ?? new List<string>();
            ChatGPTCustomModelsSrt = chatGPTCustomModelsSrt ?? new List<string>();
            ChatGPTSrtBatchSize = chatGptSrtBatchSize;
            SelectedChatGPTModelSrt = selectedChatGPTModelSrt;
            ChutesCustomModelsSrt = chutesCustomModelsSrt ?? new List<string>();
            GeminiCustomModelsSrt = geminiCustomModelsSrt ?? new List<string>();
            ChatGPTCustomModelsSrt = chatGPTCustomModelsSrt ?? new List<string>();
            BindDataToUi();
        }
        private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                this.DragMove();
        }
        private void BindDataToUi()
        {
            GoogleDriveFolderIdComboBox.Text = GoogleDriveFolderId;
            GoogleDriveFolderIdComboBox.Text = GoogleDriveFolderId;
            PathVsfTextBox.Text = PathVSF;
            OcrTextFolderNameTextBox.Text = OcrTextFolderName;
            CmdVsfArgsTextBox.Text = CmdVsfArgs;
            CustomVsfOutputBaseDirTextBox.Text = CustomVsfOutputBaseDir;
            CustomSubtitleOutputDirTextBox.Text = CustomSubtitleOutputDir;
            UISettingsVsfNumThreadsSearchEntry.Text = VsfNumThreadsSearch;
            UISettingsVsfNumThreadsCleanEntry.Text = VsfNumThreadsClean;
            UISettingsUseCudaCheck.IsChecked = UseCuda;
            UISettingsDeleteRawTextsCheck.IsChecked = DeleteRawTexts;
            UISettingsDeleteTextsCheck.IsChecked = DeleteTexts;
            UISettingsCompressRawTextsCheck.IsChecked = CompressRawTexts;
            UpdateVsfOpenMethodRadioButtons_UISettings();
            UpdateVsfProcessingModeRadioButtons_UISettings();
            GeminiApiKeysOcrTextBox.Text = string.Join(Environment.NewLine, GeminiApiKeysOcr);
            GeminiImagesPerRequestOcrTextBox.Text = GeminiImagesPerRequestOcr.ToString();
            GeminiRequestsPerMinuteOcrTextBox.Text = GeminiRequestsPerMinuteOcr.ToString();
            GeminiEnableMultiKeyOcrCheckBox.IsChecked = GeminiEnableMultiKeyOcr;
            PopulateModelComboBox(GeminiModelOcrComboBox, ApiConfig.DefaultGeminiModelOcr, GeminiCustomModelsOcr, SelectedGeminiModelOcr);
            ChutesApiKeySrtPasswordBox.Password = ChutesApiKeySrt;
            GeminiApiKeysSrtTextBox.Text = string.Join(Environment.NewLine, GeminiApiKeysSrt);
            GeminiEnableMultiKeySrtCheckBox.IsChecked = GeminiEnableMultiKeySrt;
            SrtProviderAIOLauncherRadio.IsChecked = SelectedSrtApiProvider == SrtApiProvider.AIOLauncher;
            SrtProviderChutesAIRadio.IsChecked = SelectedSrtApiProvider == SrtApiProvider.ChutesAI;
            SrtProviderGeminiRadio.IsChecked = SelectedSrtApiProvider == SrtApiProvider.Gemini;
            PopulateModelComboBox(ChutesModelSrtComboBox, ApiConfig.DefaultChutesModelSrt, ChutesCustomModelsSrt, SelectedChutesModelSrt);
            PopulateModelComboBox(GeminiModelSrtComboBox, ApiConfig.DefaultGeminiModelSrt, GeminiCustomModelsSrt, SelectedGeminiModelSrt);
            GeminiSrtRpmTextBox.Text = GeminiSrtTranslationRpm.ToString();
            GeminiSrtBatchSizeTextBox.Text = GeminiSrtTranslationBatchSize.ToString();
            GeminiSrtThinkingBudgetTextBox.Text = GeminiSrtThinkingBudget.ToString();
            SrtProviderChatGPTRadio.IsChecked = SelectedSrtApiProvider == SrtApiProvider.ChatGPT;
            PopulateModelComboBox(ChatGPTModelSrtComboBox, ApiConfig.DefaultChatGPTModelsSrt.FirstOrDefault(), ChatGPTCustomModelsSrt, SelectedChatGPTModelSrt);
            ChatGPTSrtBatchSizeTextBox.Text = ChatGPTSrtBatchSize.ToString();
            UpdateSrtApiProviderSpecificControls();
        }

        private void SaveUiToData()
        {
            // Lấy giá trị từ ComboBox (kể cả khi người dùng nhập tay)
            GoogleDriveFolderId = GoogleDriveFolderIdComboBox.Text.Trim();
            PathVSF = PathVsfTextBox.Text.Trim();
            OcrTextFolderName = OcrTextFolderNameTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(OcrTextFolderName)) OcrTextFolderName = "TXTImages";
            CmdVsfArgs = CmdVsfArgsTextBox.Text.Trim();
            CustomVsfOutputBaseDir = CustomVsfOutputBaseDirTextBox.Text.Trim();
            CustomSubtitleOutputDir = CustomSubtitleOutputDirTextBox.Text.Trim();

            if (UISettingsVsfOpenCvRadio.IsChecked == true) SelectedVsfOpenMethod = VsfVideoOpenMethod.OpenCV;
            else if (UISettingsVsfFfmpegRadio.IsChecked == true) SelectedVsfOpenMethod = VsfVideoOpenMethod.FFmpeg;
            else SelectedVsfOpenMethod = VsfVideoOpenMethod.Default;

            if (UISettingsVsfModeCleanRadio.IsChecked == true) SelectedVsfProcessingMode = VsfProcessingMode.CleanAndCreateTxtImages;
            else SelectedVsfProcessingMode = VsfProcessingMode.SearchSubtitlesOnly;

            VsfNumThreadsSearch = UISettingsVsfNumThreadsSearchEntry.Text.Trim();
            VsfNumThreadsClean = UISettingsVsfNumThreadsCleanEntry.Text.Trim();
            UseCuda = UISettingsUseCudaCheck.IsChecked ?? false;
            DeleteRawTexts = UISettingsDeleteRawTextsCheck.IsChecked ?? false;
            DeleteTexts = UISettingsDeleteTextsCheck.IsChecked ?? false;
            CompressRawTexts = UISettingsCompressRawTextsCheck.IsChecked ?? false;

            GeminiApiKeysOcr = GeminiApiKeysOcrTextBox.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).Select(k => k.Trim()).Where(k => !string.IsNullOrWhiteSpace(k)).ToList();
            GeminiImagesPerRequestOcr = int.Parse(GeminiImagesPerRequestOcrTextBox.Text.Trim());
            GeminiRequestsPerMinuteOcr = int.Parse(GeminiRequestsPerMinuteOcrTextBox.Text.Trim());
            GeminiEnableMultiKeyOcr = GeminiEnableMultiKeyOcrCheckBox.IsChecked ?? false;
            SelectedGeminiModelOcr = UpdateAndGetSelectedModel(GeminiModelOcrComboBox, ApiConfig.DefaultGeminiModelOcr, GeminiCustomModelsOcr);

            ChutesApiKeySrt = ChutesApiKeySrtPasswordBox.Password;
            GeminiApiKeysSrt = GeminiApiKeysSrtTextBox.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).Select(k => k.Trim()).Where(k => !string.IsNullOrWhiteSpace(k)).ToList();
            GeminiEnableMultiKeySrt = GeminiEnableMultiKeySrtCheckBox.IsChecked ?? false;
            if (SrtProviderAIOLauncherRadio.IsChecked == true) SelectedSrtApiProvider = SrtApiProvider.AIOLauncher;
            else if (SrtProviderChutesAIRadio.IsChecked == true) SelectedSrtApiProvider = SrtApiProvider.ChutesAI;
            else if (SrtProviderGeminiRadio.IsChecked == true) SelectedSrtApiProvider = SrtApiProvider.Gemini;
            else SelectedSrtApiProvider = SrtApiProvider.ChatGPT;
            SelectedChutesModelSrt = UpdateAndGetSelectedModel(ChutesModelSrtComboBox, ApiConfig.DefaultChutesModelSrt, ChutesCustomModelsSrt);
            SelectedGeminiModelSrt = UpdateAndGetSelectedModel(GeminiModelSrtComboBox, ApiConfig.DefaultGeminiModelSrt, GeminiCustomModelsSrt);
            GeminiSrtTranslationRpm = int.Parse(GeminiSrtRpmTextBox.Text.Trim());
            GeminiSrtTranslationBatchSize = int.Parse(GeminiSrtBatchSizeTextBox.Text.Trim());
            GeminiSrtThinkingBudget = int.Parse(GeminiSrtThinkingBudgetTextBox.Text.Trim());
            ChatGPTSrtBatchSize = int.Parse(ChatGPTSrtBatchSizeTextBox.Text.Trim());
        }

        private bool ValidateInputs()
        {
            if (!int.TryParse(UISettingsVsfNumThreadsSearchEntry.Text.Trim(), out int numSearch) || numSearch < -1)
            {
                CustomMessageBox.Show("Giá trị 'Luồng tìm Sub (AIOSubphim)' không hợp lệ. Phải là số nguyên >= -1.", "Lỗi đầu vào", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            if (!int.TryParse(UISettingsVsfNumThreadsCleanEntry.Text.Trim(), out int numClean) || numClean < -1)
            {
                CustomMessageBox.Show("Giá trị 'Luồng làm sạch (AIOSubphim)' không hợp lệ. Phải là số nguyên >= -1.", "Lỗi đầu vào", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            if (!int.TryParse(GeminiImagesPerRequestOcrTextBox.Text.Trim(), out int imgPerReqOcr) || imgPerReqOcr <= 0)
            {
                CustomMessageBox.Show("Số ảnh mỗi yêu cầu (Gemini OCR) phải là một số nguyên dương.", "Lỗi đầu vào", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            if (!int.TryParse(GeminiRequestsPerMinuteOcrTextBox.Text.Trim(), out int reqPerMinOcr) || reqPerMinOcr <= 0)
            {
                CustomMessageBox.Show("Số yêu cầu mỗi phút (Gemini OCR) phải là một số nguyên dương.", "Lỗi đầu vào", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            if (!int.TryParse(GeminiSrtRpmTextBox.Text.Trim(), out int srtRpm) || srtRpm <= 0)
            {
                CustomMessageBox.Show("Số yêu cầu mỗi phút (Gemini Dịch SRT) phải là một số nguyên dương.", "Lỗi đầu vào", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            if (!int.TryParse(GeminiSrtBatchSizeTextBox.Text.Trim(), out int srtBatchSize) || srtBatchSize <= 0)
            {
                CustomMessageBox.Show("Số dòng tối đa mỗi batch (Gemini Dịch SRT) phải là một số nguyên dương.", "Lỗi đầu vào", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            if (!int.TryParse(GeminiSrtThinkingBudgetTextBox.Text.Trim(), out int srtThinkingBudget) || srtThinkingBudget < 0)
            {
                CustomMessageBox.Show("Thinking Budget (Gemini Dịch SRT) phải là một số nguyên không âm.", "Lỗi đầu vào", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            if (!int.TryParse(ChatGPTSrtBatchSizeTextBox.Text.Trim(), out int chatGptBatchSize) || chatGptBatchSize <= 0)
            {
                CustomMessageBox.Show("Số dòng tối đa mỗi batch (ChatGPT Dịch SRT) phải là một số nguyên dương.", "Lỗi đầu vào", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            return true;
        }
        
        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            var result = CustomMessageBox.Show(
                "Bạn có chắc muốn khôi phục tất cả cài đặt về mặc định không?",
                "Xác nhận Reset",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }
            string googleDriveId = this.GoogleDriveFolderId;
            List<string> geminiKeysOcr = this.GeminiApiKeysOcr;
            string chutesKey = this.ChutesApiKeySrt;
            List<string> geminiKeysSrt = this.GeminiApiKeysSrt;
            PathVSF = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AIOSubphim.exe");
            OcrTextFolderName = "TXTImages";
            CmdVsfArgs = "";
            CustomVsfOutputBaseDir = "";
            CustomSubtitleOutputDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            SelectedVsfOpenMethod = VsfVideoOpenMethod.Default;
            SelectedVsfProcessingMode = VsfProcessingMode.CleanAndCreateTxtImages;
            VsfNumThreadsSearch = "-1";
            VsfNumThreadsClean = "-1";
            UseCuda = true;
            DeleteRawTexts = true;
            DeleteTexts = true;
            CompressRawTexts = false;

            // Gemini OCR
            GeminiImagesPerRequestOcr = 15;
            GeminiRequestsPerMinuteOcr = 8;
            GeminiEnableMultiKeyOcr = false;
            SelectedGeminiModelOcr = ApiConfig.DefaultGeminiModelOcr;
            GeminiCustomModelsOcr = new List<string>();

            // SRT Translation
            GeminiEnableMultiKeySrt = false;
            SelectedSrtApiProvider = SrtApiProvider.AIOLauncher;
            SelectedChutesModelSrt = ApiConfig.DefaultChutesModelSrt;
            SelectedGeminiModelSrt = ApiConfig.DefaultGeminiModelSrt;
            ChutesCustomModelsSrt = new List<string>();
            GeminiCustomModelsSrt = new List<string>();
            GeminiSrtTranslationRpm = 8;
            GeminiSrtTranslationBatchSize = 40;
            GeminiSrtThinkingBudget = 8192;

            GoogleDriveFolderId = googleDriveId;
            GeminiApiKeysOcr = geminiKeysOcr;
            ChutesApiKeySrt = chutesKey;
            GeminiApiKeysSrt = geminiKeysSrt;
            BindDataToUi();

            CustomMessageBox.Show("Đã khôi phục cài đặt mặc định. Nhấn 'Lưu' để áp dụng thay đổi.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInputs()) return;
            SaveUiToData();
            DialogResult = true;
            Close();
        }


        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        #region UI Helpers
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void UpdateVsfOpenMethodRadioButtons_UISettings()
        {
            UISettingsVsfOpenDefaultRadio.IsChecked = (SelectedVsfOpenMethod == VsfVideoOpenMethod.Default);
            UISettingsVsfOpenCvRadio.IsChecked = (SelectedVsfOpenMethod == VsfVideoOpenMethod.OpenCV);
            UISettingsVsfFfmpegRadio.IsChecked = (SelectedVsfOpenMethod == VsfVideoOpenMethod.FFmpeg);
        }

        private void UpdateVsfProcessingModeRadioButtons_UISettings()
        {
            UISettingsVsfModeCleanRadio.IsChecked = (SelectedVsfProcessingMode == VsfProcessingMode.CleanAndCreateTxtImages);
            UISettingsVsfModeSearchOnlyRadio.IsChecked = (SelectedVsfProcessingMode == VsfProcessingMode.SearchSubtitlesOnly);
        }

        private void PopulateModelComboBox(ComboBox comboBox, string defaultModel, List<string> customModels, string selectedModel)
        {
            comboBox.Items.Clear();
            if (!string.IsNullOrEmpty(defaultModel)) comboBox.Items.Add(defaultModel);
            foreach (var model in customModels.Where(m => m != defaultModel && !string.IsNullOrWhiteSpace(m)).Distinct())
            {
                comboBox.Items.Add(model);
            }
            if (!string.IsNullOrEmpty(selectedModel) && comboBox.Items.Contains(selectedModel))
            {
                comboBox.SelectedItem = selectedModel;
            }
            else if (comboBox.Items.Count > 0)
            {
                comboBox.SelectedIndex = 0;
            }
        }

        private string UpdateAndGetSelectedModel(ComboBox comboBox, string defaultModel, List<string> customModelsList)
        {
            string selectedModel = comboBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(selectedModel))
            {
                return defaultModel;
            }

            if (selectedModel != defaultModel && !customModelsList.Contains(selectedModel))
            {
                customModelsList.Add(selectedModel);
                customModelsList.Sort();
            }
            return selectedModel;
        }

        private void AddCustomModelSrtButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn) || !(btn.Tag is string providerTag)) return;

            ComboBox targetComboBox;
            List<string> targetCustomList;
            string defaultModel;

            if (providerTag == "ChutesAI")
            {
                targetComboBox = ChutesModelSrtComboBox;
                targetCustomList = ChutesCustomModelsSrt;
                defaultModel = ApiConfig.DefaultChutesModelSrt;
            }
            else if (providerTag == "ChatGPT")
            {
                targetComboBox = ChatGPTModelSrtComboBox;
                targetCustomList = ChatGPTCustomModelsSrt;
                defaultModel = ApiConfig.DefaultChatGPTModelsSrt.FirstOrDefault();
            }
            else if (providerTag == "Gemini")
            {
                targetComboBox = GeminiModelSrtComboBox;
                targetCustomList = GeminiCustomModelsSrt;
                defaultModel = ApiConfig.DefaultGeminiModelSrt;
            }
            else
            {
                return;
            }

            string newModelName = targetComboBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(newModelName) || newModelName == defaultModel || targetComboBox.Items.Contains(newModelName))
            {
                CustomMessageBox.Show($"Tên model '{newModelName}' không hợp lệ, là model mặc định, hoặc đã tồn tại.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            targetCustomList.Add(newModelName);
            targetComboBox.Items.Add(newModelName);
            targetComboBox.SelectedItem = newModelName;
            CustomMessageBox.Show($"Đã thêm model custom '{newModelName}'.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void AddCustomModelOcrButton_Click(object sender, RoutedEventArgs e)
        {
            ComboBox targetComboBox = GeminiModelOcrComboBox;
            List<string> targetCustomList = GeminiCustomModelsOcr;
            string defaultModel = ApiConfig.DefaultGeminiModelOcr;
            string newModelName = targetComboBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(newModelName) ||
                (!string.IsNullOrEmpty(defaultModel) && newModelName == defaultModel) ||
                targetCustomList.Contains(newModelName) ||
                targetComboBox.Items.OfType<string>().Any(i => i == newModelName))
            {
                CustomMessageBox.Show($"Tên model OCR '{newModelName}' không hợp lệ, là model mặc định, hoặc đã tồn tại.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            targetCustomList.Add(newModelName);
            targetComboBox.Items.Add(newModelName);
            targetComboBox.SelectedItem = newModelName;
            CustomMessageBox.Show($"Đã thêm model OCR custom '{newModelName}'.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
        }


        private void SrtApiProvider_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (!this.IsLoaded) return;
            UpdateSrtApiProviderSpecificControls();
        }

        private void UpdateSrtApiProviderSpecificControls()
        {
            bool isAIO = SrtProviderAIOLauncherRadio.IsChecked == true;
            bool isChutes = SrtProviderChutesAIRadio.IsChecked == true;
            bool isGemini = SrtProviderGeminiRadio.IsChecked == true;
            bool isChatGPT = SrtProviderChatGPTRadio.IsChecked == true;
            ChutesAISrtSettingsGroup.IsEnabled = !isAIO && (isChutes || isChatGPT);
            GeminiSrtSettingsGroup.IsEnabled = !isAIO && isGemini;
            ChutesModelControls.Visibility = isChutes ? Visibility.Visible : Visibility.Collapsed;
            ChatGPTModelControls.Visibility = isChatGPT ? Visibility.Visible : Visibility.Collapsed;
        }
        private void BrowseVsfButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
                Title = "Chọn file AIOSubphim.exe"
            };
            if (openFileDialog.ShowDialog() == true)
            {
                PathVsfTextBox.Text = openFileDialog.FileName;
            }
        }
        private void BrowseVsfOutputDirButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new VistaFolderBrowserDialog
            {
                Description = "Chọn thư mục Output chính cho VideoSubFinder",
                UseDescriptionForTitle = true
            };
            if (!string.IsNullOrWhiteSpace(CustomVsfOutputBaseDirTextBox.Text) && Directory.Exists(CustomVsfOutputBaseDirTextBox.Text))
            {
                dialog.SelectedPath = CustomVsfOutputBaseDirTextBox.Text;
            }
            if (dialog.ShowDialog(this).GetValueOrDefault())
            {
                CustomVsfOutputBaseDirTextBox.Text = dialog.SelectedPath;
            }
        }
        private void BrowseSubtitleOutputDirButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new VistaFolderBrowserDialog
            {
                Description = "Chọn thư mục Output cho file Subtitle",
                UseDescriptionForTitle = true
            };
            if (!string.IsNullOrWhiteSpace(CustomSubtitleOutputDirTextBox.Text) && Directory.Exists(CustomSubtitleOutputDirTextBox.Text))
            {
                dialog.SelectedPath = CustomSubtitleOutputDirTextBox.Text;
            }
            if (dialog.ShowDialog(this).GetValueOrDefault())
            {
                CustomSubtitleOutputDirTextBox.Text = dialog.SelectedPath;
            }
        }

        #endregion
    }
}