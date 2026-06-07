using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RZDTrainer.Models
{
    public class EvaluationResult
    {
        [JsonPropertyName("score")]
        public float Score { get; set; }

        [JsonPropertyName("canonical")]
        public string Canonical { get; set; } = "";

        [JsonPropertyName("directMatch")]
        public float DirectMatch { get; set; }

        [JsonPropertyName("keywordMatch")]
        public float KeywordMatch { get; set; }

        [JsonPropertyName("neuralMatch")]
        public float NeuralMatch { get; set; }

        [JsonPropertyName("foundAcceptable")]
        public bool FoundAcceptable { get; set; }

        [JsonPropertyName("isWrong")]
        public bool IsWrong { get; set; }

        [JsonPropertyName("neuralUsed")]
        public bool NeuralUsed { get; set; }

        [JsonPropertyName("error")]
        public string ErrorMessage { get; set; } = "";

        public bool IsCorrect => Score >= 75;
        public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

        public static EvaluationResult Error(string message) => new() { Score = 0, ErrorMessage = message };
    }

    public class Scenario
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Context { get; set; } = "";
        public string Canonical { get; set; } = "";
        public List<string> Keywords { get; set; } = new();
        public Dictionary<string, List<string>> Synonyms { get; set; } = new();
        public List<string> AcceptableVariants { get; set; } = new();
        public List<string> WrongVariants { get; set; } = new();
        public string ResponseFrom { get; set; } = "";
    }
}