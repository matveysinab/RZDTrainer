using System;
using System.Collections.Generic;
using System.Linq;

namespace RZDTrainer.Services
{
    public sealed class VoiceCommandEngine
    {
        private readonly VoiceCommandCatalog _catalog;
        private PendingCommand? _pending;
        
        public event Action<string> OnCommandExecuted;
        public event Action<string, string> OnNeedsConfirmation;
        public event Action<string> OnCommandCancelled;
        
        public VoiceCommandEngine(VoiceCommandCatalog catalog)
        {
            _catalog = catalog;
        }
        
        public VoiceCommandDecision Process(string recognizedText)
        {
            var text = Normalize(recognizedText);
            if (string.IsNullOrWhiteSpace(text))
                return VoiceCommandDecision.None();
            
            // 1. Критические команды
            if (MatchesCritical(text))
            {
                _pending = null;
                OnCommandExecuted?.Invoke("EMERGENCY_STOP");
                return VoiceCommandDecision.Execute("EMERGENCY_STOP", false);
            }
            
            // 2. Ожидание подтверждения
            if (_pending != null)
            {
                if (MatchesSystemConfirm(text))
                {
                    var cmd = _pending.CommandName;
                    _pending = null;
                    OnCommandExecuted?.Invoke(cmd);
                    return VoiceCommandDecision.Execute(cmd, false, true);
                }
                
                if (MatchesSystemCancel(text))
                {
                    var cmd = _pending.CommandName;
                    _pending = null;
                    OnCommandCancelled?.Invoke(cmd);
                    return VoiceCommandDecision.Cancelled(cmd);
                }
                
                return VoiceCommandDecision.WaitingConfirmation(_pending.CommandName);
            }
            
            // 3. Команды, требующие подтверждения
            var confirmMatch = FindConfirmMatch(text);
            if (confirmMatch != null)
            {
                _pending = new PendingCommand(confirmMatch.Name);
                OnNeedsConfirmation?.Invoke(confirmMatch.Name, confirmMatch.Phrases.FirstOrDefault() ?? "");
                return VoiceCommandDecision.NeedsConfirmation(confirmMatch.Name);
            }
            
            // 4. Информационные команды
            var infoMatch = FindInfoMatch(text);
            if (infoMatch != null)
            {
                OnCommandExecuted?.Invoke(infoMatch.Name);
                return VoiceCommandDecision.Execute(infoMatch.Name, false);
            }
            
            return VoiceCommandDecision.None();
        }
        
        private bool MatchesCritical(string text)
        {
            return _catalog.Critical.Any(p => text.Contains(Normalize(p)));
        }
        
        private bool MatchesSystemConfirm(string text)
        {
            return _catalog.System.Confirm.Any(p => text.Contains(Normalize(p)));
        }
        
        private bool MatchesSystemCancel(string text)
        {
            return _catalog.System.Cancel.Any(p => text.Contains(Normalize(p)));
        }
        
        private VoiceCommandItem? FindConfirmMatch(string text)
        {
            foreach (var item in _catalog.Confirm)
            {
                if (item.Phrases.Any(p => text.Contains(Normalize(p))))
                    return item;
            }
            return null;
        }
        
        private VoiceCommandItem? FindInfoMatch(string text)
        {
            foreach (var item in _catalog.Info)
            {
                if (item.Phrases.Any(p => text.Contains(Normalize(p))))
                    return item;
            }
            return null;
        }
        
        private static string Normalize(string s) => (s ?? "").Trim().ToLowerInvariant();
        
        private sealed record PendingCommand(string CommandName);
    }
    
    public sealed record VoiceCommandDecision(string Kind, string? CommandName, bool RequiresConfirm, bool FromConfirmation)
    {
        public static VoiceCommandDecision None() => new("none", null, false, false);
        public static VoiceCommandDecision NeedsConfirmation(string cmd) => new("needs_confirm", cmd, true, false);
        public static VoiceCommandDecision WaitingConfirmation(string cmd) => new("waiting", cmd, true, false);
        public static VoiceCommandDecision Execute(string cmd, bool requiresConfirm, bool fromConfirmation = false) 
            => new("execute", cmd, requiresConfirm, fromConfirmation);
        public static VoiceCommandDecision Cancelled(string cmd) => new("cancelled", cmd, false, false);
        
        public bool IsNone => Kind == "none";
        public bool IsExecute => Kind == "execute";
        public bool IsNeedsConfirm => Kind == "needs_confirm";
        public bool IsWaiting => Kind == "waiting";
        public bool IsCancelled => Kind == "cancelled";
    }
}