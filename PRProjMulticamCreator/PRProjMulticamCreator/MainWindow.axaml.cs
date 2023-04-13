using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PRProjMulticamCreator.Core;
using PRProjMulticamCreator.Core.Extensions;
using PRProjMulticamCreator.Models;
using PRProjMulticamCreator.Services;

namespace PRProjMulticamCreator;

public partial class MainWindow : Window
{
    private string PrprojPath { get; set; }
    private string Speaker1Wav { get; set; }
    private string Speaker2Wav { get; set; }

    public MainWindow()
    {
        InitializeComponent();
        StartButton.IsEnabled = false;
        FirstSpeakerSensitivity.Text = "0.055";
        SecondSpeakerSensitivity.Text = "0.065";
        DiluteIterations.Value = 3;
    }

    private async void SelectPrprojFileButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select .prproj file"
        };
        dlg.Filters?.Add(new FileDialogFilter() { Name = "Adobe Premier Pro project file", Extensions = { "prproj" } });
        var result = await dlg.ShowAsync(this);
        if (result != null)
        {
            SelectedPrprojFile.Text = Path.GetFileName(result[0]);
            PrprojPath = result[0];
            CheckStartButtonEnabled();
        }
    }

    private async void SelectSpeaker1WavButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select .wav file for first speaker"
        };
        dlg.Filters?.Add(new FileDialogFilter() { Name = ".wav", Extensions = { "wav" } });
        var result = await dlg.ShowAsync(this);
        if (result != null)
        {
            SelectedSpeaker1WavFile.Text = Path.GetFileName(result[0]);
            Speaker1Wav = result[0];
            CheckStartButtonEnabled();
        }
    }
    private async void SelectSpeaker2WavButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select .wav file for second speaker"
        };
        dlg.Filters?.Add(new FileDialogFilter() { Name = ".wav", Extensions = { "wav" } });
        var result = await dlg.ShowAsync(this);
        if (result != null)
        {
            SelectedSpeaker2WavFile.Text = Path.GetFileName(result[0]);
            Speaker2Wav = result[0];
            CheckStartButtonEnabled();
        }
    }


    private void CheckStartButtonEnabled()
    {
        if (!string.IsNullOrEmpty(PrprojPath) && !string.IsNullOrEmpty(Speaker1Wav) && !string.IsNullOrEmpty(Speaker2Wav))
        {
            StartButton.IsEnabled = true;
        }
        else
        {
            StartButton.IsEnabled = false;
        }
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        ProgressBar.IsVisible = true;

        // load XML
        var gzipService = new GzipService();
        var prproj = gzipService.LoadXml(PrprojPath);

        // save files (for Debugging)
        // await gzipService.DecompressAndSave(PrprojPath, Constants.DecompressedPath);
        // prproj.Load(Constants.DecompressedPath);

        // process frames for two Tracks
        var frameService = new FrameService();
        var primaryFrames = frameService.GetListOfPureWavFrames(
            Speaker1Wav,
            FirstSpeakerSensitivity.Text.ToDouble(),
            Constants.PremierProPrimaryTrackIndex); // get real frames from primary Track
        primaryFrames = frameService.RemoveNoiseFrames(primaryFrames);
        primaryFrames = frameService.MergeThroughSilence(primaryFrames);
        var secondarySyntheticFrames = frameService.GetSyntheticFrames(primaryFrames); // create synthetic frames for secondary Track

        var allFrames = new List<FrameModel>();
        allFrames.AddRange(primaryFrames);
        allFrames.AddRange(secondarySyntheticFrames);
        allFrames = allFrames.OrderBy(a => a.InPoint).ToList(); // sort frames by InPoint

        // mix short frames from real secondary Track
        var secondaryFrames = frameService.GetListOfPureWavFrames(
            Speaker2Wav,
            SecondSpeakerSensitivity.Text.ToDouble(),
            Constants.PremierProSecondaryTrackIndex); // get real frames from secondary Track
        secondaryFrames = frameService.RemoveNoiseFrames(secondaryFrames);
        secondaryFrames = frameService.MergeThroughSilence(secondaryFrames);
        var secondaryShortFrames = frameService.RemoveLongFrames(secondaryFrames);
        var allWithSecondaryShortFrames = frameService.AddShortFramesToAllFrames(secondaryShortFrames, allFrames); //merge short secondary frames with all frames

        var result = new List<FrameModel>();
        for (int i = 0; i < DiluteIterations.Value; i++)
        {
            result = frameService.DiluteLongFrames(allWithSecondaryShortFrames); // dilute long frames
        }

        // update multicam track
        var nodeService = new NodeService();
        nodeService.UpdateMulticamTrack(prproj, result);

        Result.Text = "Done!";
        ProgressBar.IsVisible = false;
        await ShowSaveFileDialog(prproj);

        // save files (for Debugging)
        // prproj.Save(Constants.TempFilePath);
        // gzipService.CompressAndSave(Constants.CompressedFilePath, Constants.TempFilePath);
    }

    private async Task ShowSaveFileDialog(XmlDocument xmlDoc)
    {
        SaveFileDialog saveFileDialog = new SaveFileDialog
        {
            Title = "Save .prproj File",
            DefaultExtension = "prproj",
            Filters = new List<FileDialogFilter>
            {
                new FileDialogFilter { Name = "Premiere Pro Project", Extensions = new List<string> { "prproj" } }
            }
        };

        var result = await saveFileDialog.ShowAsync(this); // 'this' refers to your window or control

        if (!string.IsNullOrEmpty(result))
        {
            await using FileStream fs = File.Create(result);
            await using GZipStream gzipStream = new GZipStream(fs, CompressionLevel.Optimal);
            await using XmlTextWriter xmlWriter = new XmlTextWriter(gzipStream, Encoding.UTF8);
            xmlDoc.Save(xmlWriter);
        }
    }
}