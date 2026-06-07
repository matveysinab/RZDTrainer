using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace RZDTrainer.Services
{
    public sealed class VoiceCommandCatalog
    {
        public List<string> Critical { get; set; } = new();
        public List<VoiceCommandItem> Confirm { get; set; } = new();
        public List<VoiceCommandItem> Info { get; set; } = new();
        public SystemCommands System { get; set; } = new();
        
        public class SystemCommands
        {
            public List<string> Confirm { get; set; } = new();
            public List<string> Cancel { get; set; } = new();
        }
        
        public static VoiceCommandCatalog LoadFromJson(string filePath)
        {
            var json = File.ReadAllText(filePath);
            var raw = JsonSerializer.Deserialize<RawVoiceCommands>(json);
            
            if (raw == null)
                throw new Exception("Не удалось загрузить voice_commands.json");
            
            return new VoiceCommandCatalog
            {
                Critical = raw.Critical ?? new List<string>(),
                Confirm = raw.Confirm?.ConvertAll(c => new VoiceCommandItem(c.Name ?? "", c.Phrases ?? new List<string>())) ?? new List<VoiceCommandItem>(),
                Info = raw.Info?.ConvertAll(i => new VoiceCommandItem(i.Name ?? "", i.Phrases ?? new List<string>())) ?? new List<VoiceCommandItem>(),
                System = new SystemCommands
                {
                    Confirm = raw.System?.Confirm ?? new List<string>(),
                    Cancel = raw.System?.Cancel ?? new List<string>()
                }
            };
        }
        
        private class RawVoiceCommands
        {
            public List<string>? Critical { get; set; }
            public List<RawCommandItem>? Confirm { get; set; }
            public List<RawCommandItem>? Info { get; set; }
            public RawSystemCommands? System { get; set; }
        }
        
        private class RawCommandItem
        {
            public string? Name { get; set; }
            public List<string>? Phrases { get; set; }
        }
        
        private class RawSystemCommands
        {
            public List<string>? Confirm { get; set; }
            public List<string>? Cancel { get; set; }
        }
    }
    
    public sealed record VoiceCommandItem(string Name, List<string> Phrases);
}