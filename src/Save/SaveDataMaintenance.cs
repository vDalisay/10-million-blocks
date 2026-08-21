using System;
using System.IO;
using Godot;

namespace TenMillionBlocks.Save;

/// <summary>
/// Explicit destructive maintenance entry point used by the temporary main-menu Settings screen.
/// Development saves, temporary save files and replay history are removed together so testers can
/// reliably restart the authored progression from the first tutorial.
/// </summary>
public static class SaveDataMaintenance
{
    public static void ClearAllLocalData()
    {
        DeleteUserFile(SaveService.DefaultPath);
        DeleteUserFile(SaveService.DefaultPath + ".tmp");
        DeleteUserFile(SaveService.LegacyV2Path);
        DeleteUserFile(SaveService.LegacyV2Path + ".tmp");

        string replayDirectory = ProjectSettings.GlobalizePath("user://replays");
        if (Directory.Exists(replayDirectory))
        {
            Directory.Delete(replayDirectory, recursive: true);
        }
    }

    private static void DeleteUserFile(string path)
    {
        string absolute = ProjectSettings.GlobalizePath(path);
        if (File.Exists(absolute)) File.Delete(absolute);
    }
}
