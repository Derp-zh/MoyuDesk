using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Taskboard.Desktop;

// Device-local first use is outside task/configuration data and has no app-version key.
internal sealed class DeviceOnboarding
{
    private sealed class Record
    {
        public Record() { }
        [System.Text.Json.Serialization.JsonRequired]
        public string Format { get; set; } = "taskboard-device-first-use";
        [System.Text.Json.Serialization.JsonRequired]
        public int Version { get; set; } = 1;
        [System.Text.Json.Serialization.JsonRequired]
        public Dictionary<string, string> Machines { get; set; } = [];
    }
    internal string MachineId { get; }
    private readonly string directory;
    internal DeviceOnboarding(string directory, string machineId)
    {
        this.directory = Path.GetFullPath(directory);
        if (string.IsNullOrWhiteSpace(machineId) || machineId.Length > 128) throw new ArgumentException("Invalid device identity");
        MachineId = machineId;
    }
    internal (bool Show, string Error) Claim(bool alreadyUsedOnMachine)
    {
        try
        {
            Directory.CreateDirectory(directory);
            using var claim = new FileStream(Path.Combine(directory, "first-use.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var path = Path.Combine(directory, "first-use.json");
            var record = File.Exists(path) ? JsonSerializer.Deserialize<Record>(File.ReadAllText(path)) ?? throw new InvalidDataException("首次使用记录为空") : new Record();
            if (record.Format != "taskboard-device-first-use" || record.Version != 1 || record.Machines is null || record.Machines.Count > 256 ||
                record.Machines.Any(x => string.IsNullOrWhiteSpace(x.Key) || x.Key.Length > 128 || x.Value is not ("offered" or "existing-installation")))
                throw new InvalidDataException("首次使用记录格式无效，已保留原文件");
            if (record.Machines.ContainsKey(MachineId)) return (false, "");
            record.Machines.Add(MachineId, alreadyUsedOnMachine ? "existing-installation" : "offered");
            var temporary = Path.Combine(directory, "first-use.pending-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { JsonSerializer.Serialize(file, record); file.Flush(true); }
                File.Move(temporary, path, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return (!alreadyUsedOnMachine, "");
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        { return (false, "本机首次使用记录未能保存：" + e.Message); }
    }
    internal (bool Reset, string Error) Reset()
    {
        try
        {
            var path = Path.Combine(directory, "first-use.json");
            if (!Directory.Exists(directory) || !File.Exists(path)) return (true, "");
            using var claim = new FileStream(Path.Combine(directory, "first-use.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var record = JsonSerializer.Deserialize<Record>(File.ReadAllText(path)) ?? throw new InvalidDataException("首次使用记录为空");
            if (record.Format != "taskboard-device-first-use" || record.Version != 1 || record.Machines is null || record.Machines.Count > 256 ||
                record.Machines.Any(x => string.IsNullOrWhiteSpace(x.Key) || x.Key.Length > 128 || x.Value is not ("offered" or "existing-installation")))
                throw new InvalidDataException("首次使用记录格式无效，已保留原文件");
            if (!record.Machines.Remove(MachineId)) return (true, "");
            var temporary = Path.Combine(directory, "first-use.pending-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { JsonSerializer.Serialize(file, record); file.Flush(true); }
                File.Move(temporary, path, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return (true, "");
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        { return (false, "本机首次使用记录未能重置：" + e.Message); }
    }
}
