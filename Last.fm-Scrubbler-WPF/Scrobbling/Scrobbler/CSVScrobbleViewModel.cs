﻿using IF.Lastfm.Core.Api.Enums;
using IF.Lastfm.Core.Objects;
using Scrubbler.Helper;
using Scrubbler.Properties;
using Scrubbler.Scrobbling.Data;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Scrubbler.Scrobbling.Scrobbler
{
  /// <summary>
  /// Scrobble mode.
  /// </summary>
  public enum CSVScrobbleMode
  {
    /// <summary>
    /// Scrobble the tracks based on the parsed timestamp.
    /// </summary>
    Normal,

    /// <summary>
    /// Set the timestamp by setting <see cref="ScrobbleTimeViewModel.Time"/>.
    /// </summary>
    ImportMode
  }

  /// <summary>
  /// ViewModel for the <see cref="CSVScrobbleView"/>.
  /// </summary>
  public class CSVScrobbleViewModel : ScrobbleMultipleTimeViewModelBase<ParsedCSVScrobbleViewModel>
  {
    #region Properties

    /// <summary>
    /// The path to the csv file.
    /// </summary>
    public string CSVFilePath
    {
      get { return _csvFilePath; }
      set
      {
        _csvFilePath = value;
        NotifyOfPropertyChange();
      }
    }
    private string _csvFilePath;

    /// <summary>
    /// The selected <see cref="CSVScrobbleMode"/>.
    /// </summary>
    public CSVScrobbleMode ScrobbleMode
    {
      get { return _scrobbleMode; }
      set
      {
        _scrobbleMode = value;

        if (Scrobbles.Count > 0)
        {
          if (_windowManager.MessageBoxService.ShowDialog("Do you want to switch the Scrobble Mode? The CSV file will be parsed again!",
                                                          "Change Scrobble Mode", IMessageBoxServiceButtons.YesNo) == IMessageBoxServiceResult.Yes)
            ParseCSVFile().Forget();
        }

        NotifyOfPropertyChange();
      }
    }
    private CSVScrobbleMode _scrobbleMode;

    /// <summary>
    /// Duration between scrobbles in seconds.
    /// </summary>
    public int Duration
    {
      get { return _duration; }
      set
      {
        _duration = value;
        NotifyOfPropertyChange();
      }
    }
    private int _duration;

    #endregion Properties

    #region Member

    /// <summary>
    /// Different formats to try in case TryParse fails.
    /// </summary>
    private static readonly string[] _formats = new string[] { "M/dd/yyyy h:mm" };

    /// <summary>
    /// The factory used to create <see cref="ITextFieldParser"/>.
    /// </summary>
    private readonly ITextFieldParserFactory _parserFactory;

    /// <summary>
    /// FileOperator used to write to disk.
    /// </summary>
    private readonly IFileOperator _fileOperator;

    #endregion Member

    /// <summary>
    /// Constructor.
    /// </summary>
    /// <param name="windowManager">WindowManager used to display dialogs.</param>
    /// <param name="parserFactory">The factory used to create <see cref="ITextFieldParser"/>.</param>
    /// <param name="fileOperator">FileOperator used to write to disk.</param>
    public CSVScrobbleViewModel(IExtendedWindowManager windowManager, ITextFieldParserFactory parserFactory, IFileOperator fileOperator)
      : base(windowManager, "CSV Scrobbler")
    {
      _parserFactory = parserFactory;
      _fileOperator = fileOperator;
      Scrobbles = new ObservableCollection<ParsedCSVScrobbleViewModel>();
      Duration = 1;
      ScrobbleMode = CSVScrobbleMode.ImportMode;
    }

    /// <summary>
    /// Shows a dialog to open a csv file.
    /// </summary>
    public void LoadCSVFileDialog()
    {
      IOpenFileDialog ofd = _windowManager.CreateOpenFileDialog();
      ofd.Filter = "CSV Files|*.csv";
      if (ofd.ShowDialog())
        CSVFilePath = ofd.FileName;
    }

    /// <summary>
    /// Loads and parses a csv file.
    /// </summary>
    /// <returns>Task.</returns>
    public async Task ParseCSVFile()
    {
      try
      {
        EnableControls = false;
        OnStatusUpdated("Reading CSV file...");

        IEnumerable<string> errors = null;
        ObservableCollection<ParsedCSVScrobbleViewModel> parsedScrobbles = null;

        await Task.Run(() =>
        {
          var pparser = new CSVFileParser();
          var res = pparser.Parse(CSVFilePath, ScrobbleMode);
          errors = res.Errors;
          parsedScrobbles = new ObservableCollection<ParsedCSVScrobbleViewModel>(res.Scrobbles.Select(i => new ParsedCSVScrobbleViewModel(i, ScrobbleMode)));
        });

        if (errors.Count() == 0)
          OnStatusUpdated($"Successfully parsed CSV file. Parsed {parsedScrobbles.Count} rows");
        else
        {
          OnStatusUpdated($"Partially parsed CSV file. {errors.Count()} rows could not be parsed");
          if (_windowManager.MessageBoxService.ShowDialog("Some rows could not be parsed. Do you want to save a text file with the rows that could not be parsed?",
                                                          "Error parsing rows", IMessageBoxServiceButtons.YesNo) == IMessageBoxServiceResult.Yes)
          {
            IFileDialog sfd = _windowManager.CreateSaveFileDialog();
            sfd.Filter = "Text Files|*.txt";
            if (sfd.ShowDialog())
              _fileOperator.WriteAllLines(sfd.FileName, errors.ToArray());
          }
        }

        Scrobbles = parsedScrobbles;

      }
      catch (Exception ex)
      {
        Scrobbles.Clear();
        OnStatusUpdated($"Error parsing CSV file: {ex.Message}");
      }
      finally
      {
        EnableControls = true;
      }
    }

    /// <summary>
    /// Scrobbles the selected scrobbles.
    /// </summary>
    /// <returns>Task.</returns>
    public override async Task Scrobble()
    {
      try
      {
        EnableControls = false;
        OnStatusUpdated("Trying to scrobble selected tracks...");

        var response = await Scrobbler.ScrobbleAsync(CreateScrobbles());
        if (response.Success && response.Status == LastResponseStatus.Successful)
          OnStatusUpdated("Successfully scrobbled selected tracks");
        else
          OnStatusUpdated($"Error while scrobbling selected tracks: {response.Status}");
      }
      catch (Exception ex)
      {
        OnStatusUpdated($"Fatal error while scrobbling selected tracks: {ex.Message}");
      }
      finally
      {
        EnableControls = true;
      }
    }

    /// <summary>
    /// Create a list with tracks that will be scrobbled.
    /// </summary>
    /// <returns>List with scrobbles.</returns>
    protected override IEnumerable<Scrobble> CreateScrobbles()
    {
      var scrobbles = new List<Scrobble>();

      if (ScrobbleMode == CSVScrobbleMode.Normal)
      {
        foreach (var vm in Scrobbles.Where(i => i.ToScrobble))
        {
          scrobbles.Add(new Scrobble(vm.ArtistName, vm.AlbumName, vm.TrackName, vm.Played)
          { AlbumArtist = vm.AlbumArtist, Duration = vm.Duration });
        }
      }
      else if (ScrobbleMode == CSVScrobbleMode.ImportMode)
      {
        DateTime time = ScrobbleTimeVM.Time;
        foreach (var vm in Scrobbles.Where(i => i.ToScrobble))
        {
          scrobbles.Add(new Scrobble(vm.ArtistName, vm.AlbumName, vm.TrackName, time)
          {
            AlbumArtist = vm.AlbumArtist,
            Duration = vm.Duration
          });

          time = time.Subtract(TimeSpan.FromSeconds(Duration));
        }
      }

      return scrobbles;
    }

    /// <summary>
    /// Opens the <see cref="ConfigureCSVParserView"/>
    /// </summary>
    public void OpenCSVParserSettings()
    {
      _windowManager.ShowDialog(new ConfigureCSVParserViewModel());
    }

    /// <summary>
    /// Marks all scrobbles as "ToScrobble".
    /// </summary>
    public override void CheckAll()
    {
      SetToScrobbleState(Scrobbles.Where(i => i.IsEnabled), true);
    }

    /// <summary>
    /// Marks all scrobbles as not "ToScrobble".
    /// </summary>
    public override void UncheckAll()
    {
      SetToScrobbleState(Scrobbles.Where(i => i.IsEnabled), false);
    }

    /// <summary>
    /// Marks all selected scrobbles as "ToScrobble".
    /// </summary>
    public override void CheckSelected()
    {
      SetToScrobbleState(Scrobbles.Where(i => i.IsSelected && i.IsEnabled), true);
    }

    /// <summary>
    /// Marks all selected scrobbles as not "ToScrobble".
    /// </summary>
    public override void UncheckSelected()
    {
      SetToScrobbleState(Scrobbles.Where(i => i.IsSelected && i.IsEnabled), false);
    }
  }
}