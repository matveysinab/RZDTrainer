using System;
using System.Threading.Tasks;
using RZDTrainer.Models;

namespace RZDTrainer.Services
{
    public sealed class TrainerEngine
    {
        private readonly PythonNeuralEvaluator _neuralEvaluator;
        private readonly ScenarioLoader _scenarioLoader;
        private Scenario? _currentScenario;
        
        public event Action<Scenario> OnScenarioLoaded;
        public event Action<EvaluationResult, string> OnEvaluationComplete;
        
        public TrainerEngine(PythonNeuralEvaluator neuralEvaluator, ScenarioLoader scenarioLoader)
        {
            _neuralEvaluator = neuralEvaluator;
            _scenarioLoader = scenarioLoader;
        }
        
        public void LoadScenario(int index)
        {
            _currentScenario = _scenarioLoader.GetScenario(index);
            OnScenarioLoaded?.Invoke(_currentScenario);
        }
        
        public void LoadNextScenario()
        {
            var nextIndex = _scenarioLoader.CurrentIndex + 1;
            if (nextIndex < _scenarioLoader.TotalScenarios)
            {
                LoadScenario(nextIndex);
            }
        }
        
        public void ResetCurrentScenario()
        {
            if (_currentScenario != null)
            {
                OnScenarioLoaded?.Invoke(_currentScenario);
            }
        }
        
        public async Task<EvaluationResult> EvaluateUserPhrase(string recognizedText)
        {
            if (_currentScenario == null)
            {
                return EvaluationResult.Error("Нет загруженного сценария");
            }
            
            var result = await _neuralEvaluator.Evaluate(recognizedText, _currentScenario.Id);
            OnEvaluationComplete?.Invoke(result, recognizedText);
            return result;
        }
        
        public Scenario? GetCurrentScenario() => _currentScenario;
    }
}