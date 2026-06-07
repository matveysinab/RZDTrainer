import sys
import json
import os
from rapidfuzz import fuzz

# Принудительно устанавливаем UTF-8 для вывода
sys.stdout = open(sys.stdout.fileno(), mode='w', encoding='utf-8', buffering=1)

# Определяем пути
CURRENT_DIR = os.path.dirname(os.path.abspath(__file__))
BASE_DIR = os.path.dirname(CURRENT_DIR)

SCENARIOS_PATH = os.path.join(BASE_DIR, 'scenarios.json')
MODEL_PATH = os.path.join(BASE_DIR, 'trained_models', 'rzd_sbert_model')

# Отладка в stderr
def log(msg):
    print(msg, file=sys.stderr, flush=True)

log(f"BASE_DIR: {BASE_DIR}")
log(f"SCENARIOS_PATH: {SCENARIOS_PATH}")
log(f"MODEL_PATH: {MODEL_PATH}")
log(f"Scenarios exists: {os.path.exists(SCENARIOS_PATH)}")
log(f"Model exists: {os.path.exists(MODEL_PATH)}")

# Загружаем нейросеть
USE_NEURAL = False
model = None

try:
    from sentence_transformers import SentenceTransformer
    from sentence_transformers.util import cos_sim
    
    if os.path.exists(MODEL_PATH):
        log("Loading neural model...")
        model = SentenceTransformer(MODEL_PATH)
        USE_NEURAL = True
        log("Neural model loaded!")
    else:
        log("Model not found")
except Exception as e:
    log(f"Error loading model: {e}")

# Загружаем сценарии
DATA = {"scenarios": []}
try:
    if os.path.exists(SCENARIOS_PATH):
        with open(SCENARIOS_PATH, 'r', encoding='utf-8') as f:
            DATA = json.load(f)
        log(f"Loaded {len(DATA.get('scenarios', []))} scenarios")
except Exception as e:
    log(f"Error loading scenarios: {e}")

def evaluate(phrase, scenario_id):
    scenario = next((s for s in DATA.get('scenarios', []) if s.get('id') == scenario_id), None)
    if not scenario:
        return {
            'score': 0,
            'canonical': 'Scenario not found',
            'directMatch': 0,
            'keywordMatch': 0,
            'neuralMatch': 0,
            'foundAcceptable': False,
            'isWrong': False,
            'neuralUsed': USE_NEURAL,
            'error': f'Scenario {scenario_id} not found'
        }
    
    canonical = scenario.get('canonical', '')
    if not canonical:
        return {
            'score': 0,
            'canonical': 'No canonical phrase',
            'directMatch': 0,
            'keywordMatch': 0,
            'neuralMatch': 0,
            'foundAcceptable': False,
            'isWrong': False,
            'neuralUsed': USE_NEURAL,
            'error': 'No canonical phrase'
        }
    
    phrase_lower = phrase.lower()
    canonical_lower = canonical.lower()
    
    direct_score = fuzz.ratio(phrase_lower, canonical_lower)
    token_score = fuzz.token_sort_ratio(phrase_lower, canonical_lower)
    
    keywords = scenario.get('keywords', [])
    keywords_found = sum(1 for kw in keywords if kw in phrase_lower)
    keyword_score = (keywords_found / len(keywords)) * 100 if keywords else 100
    
    neural_score = 0
    if USE_NEURAL and model:
        try:
            emb1 = model.encode(phrase)
            emb2 = model.encode(canonical)
            neural_score = cos_sim(emb1, emb2).item() * 100
        except Exception as e:
            log(f"Neural error: {e}")
    
    acceptable = scenario.get('acceptable_variants', [])
    found_acceptable = any(fuzz.ratio(phrase_lower, a.lower()) > 85 for a in acceptable)
    
    wrong = scenario.get('wrong_variants', [])
    is_wrong = any(w in phrase_lower or fuzz.ratio(phrase_lower, w.lower()) > 80 for w in wrong)
    
    if found_acceptable:
        final_score = 100
    elif is_wrong:
        final_score = max(0, direct_score - 30)
    elif USE_NEURAL and neural_score > 0:
        final_score = neural_score * 0.5 + ((direct_score + token_score) / 2) * 0.3 + keyword_score * 0.2
    else:
        final_score = ((direct_score + token_score) / 2) * 0.7 + keyword_score * 0.3
    
    return {
        'score': round(final_score, 1),
        'canonical': canonical,
        'directMatch': round(direct_score, 1),
        'keywordMatch': round(keyword_score, 1),
        'neuralMatch': round(neural_score, 1),
        'foundAcceptable': found_acceptable,
        'isWrong': is_wrong,
        'neuralUsed': USE_NEURAL,
        'error': ''
    }

if __name__ == '__main__':
    if len(sys.argv) < 2:
        print(json.dumps({'error': 'No input file', 'score': 0}, ensure_ascii=False))
        sys.exit(1)
    
    try:
        input_file = sys.argv[1]
        with open(input_file, 'r', encoding='utf-8') as f:
            data = json.load(f)
        
        result = evaluate(data.get('phrase', ''), data.get('scenario_id', ''))
        # Ключевой момент: ensure_ascii=False для русских букв
        print(json.dumps(result, ensure_ascii=False))
    except Exception as e:
        log(f"Fatal error: {e}")
        print(json.dumps({'error': str(e), 'score': 0}, ensure_ascii=False))