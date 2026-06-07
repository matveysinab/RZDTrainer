using System.Collections.Generic;

namespace RZDTrainer.Models
{
    // Модели для voice_commands.json
    public class VoiceCommandRoot
    {
        public List<string> Critical { get; set; } = new();
        public List<VoiceCommandItem> Confirm { get; set; } = new();
        public List<VoiceCommandItem> Info { get; set; } = new();
        public VoiceCommandSystem System { get; set; } = new();
    }
    
    public class VoiceCommandItem
    {
        public string Name { get; set; } = "";
        public List<string> Phrases { get; set; } = new();
    }
    
    public class VoiceCommandSystem
    {
        public List<string> Confirm { get; set; } = new();
        public List<string> Cancel { get; set; } = new();
    }
}