using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx;
using Discord;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TaikoDiscordRPC;

[BepInPlugin("ca.sanae.taikodisgd", "TaikoDiscord", "1.0.0")]
public class TaikoDiscordPlugin : BaseUnityPlugin {
    public enum CurrentScene {
        Boot,
        TitleScreen,
        SongSelect,
        Solo,
        TestScene,
        TestSceneAdjust,
        Transition,
        Tutorial,
        Versussy, //what is this
        Tuning,
        Settings,
        Shop,
        Customization,
        RankedMenu,
        Credits,
        Calibration,
        Local2Player,
        Online
    }

    public static Discord.Discord Client = new Discord.Discord(940466512962129951, (ulong) CreateFlags.Default);
    public static CurrentScene Current = CurrentScene.TitleScreen;

    private void Awake() {
        Client.SetLogHook(LogLevel.Debug, (level, message) => Logger.Log(level switch {
            LogLevel.Error => BepInEx.Logging.LogLevel.Error,
            LogLevel.Warn => BepInEx.Logging.LogLevel.Warning,
            LogLevel.Info => BepInEx.Logging.LogLevel.Info,
            LogLevel.Debug => BepInEx.Logging.LogLevel.Debug,
            _ => BepInEx.Logging.LogLevel.All
        }, message));

        // new Harmony("ca.sanae.taikodisgd").Patch(
        //     AccessTools.PropertySetter(typeof(EnsoPlayingParameter), nameof(EnsoPlayingParameter.IsPause)),
        //     new HarmonyMethod(AccessTools.Method(GetType(), nameof(HandlePause))
        //     )
        // );

        SceneManager.sceneLoaded += SceneLoaded;
    }

    // public static void HandlePause([HarmonyArgument(0)] ref bool isPaused) {
    //     if (isPaused) {
    //         
    //     }
    // }

    private void SceneLoaded(Scene scene, LoadSceneMode mode) {
        // cleanup old stuff
        SelectManager = null;
        EnsoManager = null;
        LastGenre = EnsoData.SongGenre.Num;
        UpdatedGeneric = false;

        CurrentScene lastScene = Current;
        Current = scene.name switch {
            "Title" => CurrentScene.TitleScreen,
            "SongSelect" => CurrentScene.SongSelect,
            "Enso" => CurrentScene.Solo,
            "Tutorial" => CurrentScene.Tutorial,
            "EnsoVS" => CurrentScene.Versussy,
            "EnsoTune" => CurrentScene.Tuning,
            "GameSettings" => CurrentScene.Settings,
            "Shop" => CurrentScene.Shop,
            "Customize" => CurrentScene.Customization,
            "RankedMatch" => CurrentScene.RankedMenu,
            "Credit" => CurrentScene.Credits,
            "EnsoTestX" => CurrentScene.Calibration,
            "EnsoLocal" => CurrentScene.Local2Player,
            "EnsoRankedMatch" => CurrentScene.Online,
            _ => Current
        };
        switch (Current) {
            case CurrentScene.TitleScreen:
                UpdateNotEnso("On the title screen");
                break;
            case CurrentScene.Credits:
                UpdateNotEnso("Playing the credits slide puzzle");
                break;
            case CurrentScene.Shop:
                UpdateNotEnso("Shopping");
                break;
            case CurrentScene.Customization:
                UpdateNotEnso("Customizing Don-chan");
                break;
            case CurrentScene.Tutorial:
                UpdateNotEnso("Reading the tutorial");
                break;
            case CurrentScene.Calibration:
                UpdateNotEnso("Calibrating input");
                break;
            case CurrentScene.RankedMenu:
                UpdateNotEnso("On the ranked menu");
                break;
            case CurrentScene.Settings:
                UpdateNotEnso("Changing settings");
                break;
            case CurrentScene.SongSelect: {
                GetBaseActivity(out Activity activity);
                activity.State = "Selecting a song";
                SelectManager = FindObjectOfType<SongSelectManager>();
                LastGenre = EnsoData.SongGenre.Num;
                UpdateActivity(activity);
                break;
            }
            case CurrentScene.Solo:
            case CurrentScene.Local2Player:
            case CurrentScene.Online: {
                if (Current == lastScene) {
                    // still playing the same song
                    UpdatedGeneric = true;
                    break;
                }

                GetBaseActivity(out Activity activity);
                activity.State = "Loading a song (" + Current switch {
                    CurrentScene.Solo => "Singleplayer",
                    CurrentScene.Local2Player => "Local Multiplayer",
                    CurrentScene.Online => "Online",
                    _ => "Unknown"
                } + ")";
                EnsoManager = FindObjectOfType<EnsoGameManager>();
                UpdateActivity(activity);
                break;
            }
        }

        Logger.LogWarning(Current);
    }

    public EnsoData.SongGenre LastGenre;
    public SongSelectManager? SelectManager;
    public EnsoGameManager? EnsoManager;
    public bool UpdatedGeneric;
    public bool IsPaused = false;
    public static FieldInfo EnsoSettings => AccessTools.Field(typeof(EnsoGameManager), nameof(EnsoGameManager.settings));
    public static FieldInfo EnsoState => AccessTools.Field(typeof(EnsoGameManager), nameof(EnsoGameManager.state));
    public static FieldInfo EnsoParam => AccessTools.Field(typeof(EnsoGameManager), nameof(EnsoGameManager.ensoParam));

    private void Update() {
        Client.RunCallbacks();
    }

    // SongInfoPlayer.GetSongName
    private string GetSongName(string id) {
        Regex regex = new Regex("song_");
        Dictionary<string, string> dictionary = TaikoSingletonMonoBehaviour<CommonObjects>.Instance.MyDataManager.WordData.wordListInfoAccessers
            .Where(listInfo => Regex.IsMatch(listInfo.Key, "song_"))
            .ToDictionary(listInfo => regex.Replace(listInfo.Key, ""), listInfo => listInfo.Text);

        return dictionary[id];
    }

    // Doesn't need to be run every frame :)
    private void FixedUpdate() {
        switch (Current) {
            case CurrentScene.SongSelect: {
                if (SelectManager is null) return;
                EnsoData.SongGenre genre = (EnsoData.SongGenre) SelectManager.SongList[SelectManager.SelectedSongIndex].SongGenre;
                if (LastGenre != genre) {
                    GetGameActivity(out Activity activity, genre);
                    activity.State = "Selecting a song";
                    UpdateActivity(activity);
                }

                break;
            }
            case CurrentScene.Solo:
            case CurrentScene.Local2Player:
            case CurrentScene.Online: {
                if (EnsoManager is not null && ((EnsoPlayingParameter) EnsoParam.GetValue(EnsoManager)).IsPause != IsPaused) {
                    IsPaused = ((EnsoPlayingParameter) EnsoParam.GetValue(EnsoManager)).IsPause;
                    
                    UpdatedGeneric = false;
                }
                if (EnsoManager is not null && (EnsoGameManager.State) EnsoState.GetValue(EnsoManager) > EnsoGameManager.State.ToExec && ((EnsoPlayingParameter)EnsoParam.GetValue(EnsoManager)).GetFrameResults().lastOnpuEndTime > 0 && !UpdatedGeneric) {
                    EnsoData.Settings settings = (EnsoData.Settings) EnsoSettings.GetValue(EnsoManager);
                    GetGameActivity(out Activity activity, settings.genre);
                    if (!IsPaused) {
                        activity.Timestamps.Start = (long) DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds - (long)((EnsoPlayingParameter) EnsoParam.GetValue(EnsoManager)).TotalTime / 1000;
                        activity.Timestamps.End = activity.Timestamps.Start + ((EnsoPlayingParameter) EnsoParam.GetValue(EnsoManager)).GetFrameResults().lastOnpuEndTime / 1000;
                    }

                    activity.State = $"Playing {GetSongName(settings.musicuid)} (" + Current switch {
                        CurrentScene.Solo => "Single Player",
                        CurrentScene.Local2Player => "Local Multi Player",
                        CurrentScene.Online => "Online",
                        _ => "Unknown"
                    } + ")";

                    activity.Assets.SmallImage = settings.ensoPlayerSettings[0].courseType switch {
                        EnsoData.EnsoLevelType.Easy => "diffeasy",
                        EnsoData.EnsoLevelType.Normal => "diffnormal",
                        EnsoData.EnsoLevelType.Hard => "diffhard",
                        EnsoData.EnsoLevelType.Mania => "diffextreme",
                        EnsoData.EnsoLevelType.Ura => "diffsextreme",
                        _ => "shapiro"
                    };
                    if (Current == CurrentScene.Solo) {
                        activity.Details = activity.Assets.SmallText = settings.ensoPlayerSettings[0].courseType switch {
                            EnsoData.EnsoLevelType.Easy => "Easy",
                            EnsoData.EnsoLevelType.Normal => "Normal",
                            EnsoData.EnsoLevelType.Hard => "Hard",
                            EnsoData.EnsoLevelType.Mania => "Extreme",
                            EnsoData.EnsoLevelType.Ura => "Secret Extreme",
                            _ => "Unknown Difficulty"
                        };
                    } else {
                        activity.Details = activity.Assets.SmallImage = settings.ensoPlayerSettings[0].courseType switch {
                            EnsoData.EnsoLevelType.Easy => "Easy",
                            EnsoData.EnsoLevelType.Normal => "Normal",
                            EnsoData.EnsoLevelType.Hard => "Hard",
                            EnsoData.EnsoLevelType.Mania => "Extreme",
                            EnsoData.EnsoLevelType.Ura => "Secret Extreme",
                            _ => "Unknown Difficulty"
                        } + " vs " + settings.ensoPlayerSettings[1].courseType switch {
                            EnsoData.EnsoLevelType.Easy => "Easy",
                            EnsoData.EnsoLevelType.Normal => "Normal",
                            EnsoData.EnsoLevelType.Hard => "Hard",
                            EnsoData.EnsoLevelType.Mania => "Extreme",
                            EnsoData.EnsoLevelType.Ura => "Secret Extreme",
                            _ => "Unknown Difficulty"
                        };
                    }

                    UpdateActivity(activity);
                    UpdatedGeneric = true;
                }

                break;
            }
        }
    }

    public void GetBaseActivity(out Activity activity) {
        activity = new Activity {
            Type = ActivityType.Playing, Assets = new ActivityAssets { LargeImage = "don" }, Details = "Not playing a song"
        };
    }

    public void GetGameActivity(out Activity activity, EnsoData.SongGenre genre) {
        GetBaseActivity(out activity);
        activity.Details = "Genre: " + (activity.Assets.LargeText = genre switch {
            EnsoData.SongGenre.Pops => "Pop",
            EnsoData.SongGenre.Anime => "Anime",
            EnsoData.SongGenre.Vocalo => "VOCALOID™ Music",
            EnsoData.SongGenre.Variety => "Variety",
            EnsoData.SongGenre.Children => "Children",
            EnsoData.SongGenre.Classic => "Classical",
            EnsoData.SongGenre.Game => "Game Music",
            EnsoData.SongGenre.Namco => "Namco Original",
            _ => "Unknown Genre"
        });
        activity.Assets.LargeImage = genre switch {
            EnsoData.SongGenre.Pops => "genrepop",
            EnsoData.SongGenre.Anime => "genreanime",
            EnsoData.SongGenre.Vocalo => "genrevocaloid",
            EnsoData.SongGenre.Variety => "genrevariety",
            EnsoData.SongGenre.Classic => "genreclassic",
            EnsoData.SongGenre.Game => "genregame",
            EnsoData.SongGenre.Namco => "genrenamco",
            _ => "shapiro",
        };
    }

    public void UpdateNotEnso(string location) {
        UpdateActivity(new Activity {
            Type = ActivityType.Playing, Assets = new ActivityAssets { LargeImage = "don" }, State = location, Details = "Not playing a song"
        });
    }

    public void UpdateActivity(Activity activity) {
        Client.GetActivityManager().UpdateActivity(activity, result => {
            if (result != Result.Ok) {
                Logger.LogError("Activity failed to update");
            }
        });
    }
}