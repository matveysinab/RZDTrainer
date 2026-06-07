import json
import os
from sentence_transformers import SentenceTransformer, InputExample, losses
from torch.utils.data import DataLoader

# Определяем путь к папке с моделью (относительно текущего скрипта)
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
MODEL_DIR = os.path.join(SCRIPT_DIR, '..', '..', 'bin', 'Debug', 'net8.0-windows', 'trained_models', 'rzd_sbert_model')
MODEL_DIR = os.path.normpath(MODEL_DIR)

print(f"Модель будет сохранена в: {MODEL_DIR}")

def load_training_data(json_path):
    with open(json_path, 'r', encoding='utf-8') as f:
        data = json.load(f)
    
    positive_pairs = []
    negative_pairs = []
    
    for scenario in data['scenarios']:
        canonical = scenario['canonical']
        
        for alt in scenario.get('acceptable_variants', []):
            positive_pairs.append((canonical, alt))
        
        for wrong in scenario.get('wrong_variants', []):
            negative_pairs.append((canonical, wrong))
    
    return positive_pairs, negative_pairs

def prepare_training_examples(positive_pairs, negative_pairs):
    examples = []
    
    for text1, text2 in positive_pairs:
        examples.append(InputExample(texts=[text1, text2], label=1.0))
    
    for text1, text2 in negative_pairs:
        examples.append(InputExample(texts=[text1, text2], label=0.0))
    
    return examples

def train_model(examples, output_path):
    print("=" * 60)
    print("🚂 ОБУЧЕНИЕ НЕЙРОСЕТИ ДЛЯ ТРЕНАЖЕРА СОСТАВИТЕЛЯ")
    print("=" * 60)
    
    print(f"\n📊 Загружено {len(examples)} примеров для обучения")
    
    if len(examples) < 10:
        print("⚠️ Мало примеров для обучения!")
        return None
    
    print("\n🧠 Загрузка предобученной модели...")
    model = SentenceTransformer('paraphrase-multilingual-MiniLM-L12-v2')
    
    train_dataloader = DataLoader(examples, shuffle=True, batch_size=8)
    train_loss = losses.CosineSimilarityLoss(model)
    
    print("\n🏋️ Начинаем обучение...")
    
    model.fit(
        train_objectives=[(train_dataloader, train_loss)],
        epochs=10,
        warmup_steps=100,
        show_progress_bar=True
    )
    
    # Создаём папку и сохраняем модель
    os.makedirs(output_path, exist_ok=True)
    model.save(output_path)
    print(f"\n✅ Модель сохранена в {output_path}")
    
    return model

def test_model(model):
    print("\n" + "=" * 60)
    print("🧪 ТЕСТИРОВАНИЕ МОДЕЛИ")
    print("=" * 60)
    
    test_cases = [
        ("Ограничение 60", "Ограничение скорости 60 километров в час"),
        ("Ограничение 40", "Ограничение скорости 60 километров в час"),
    ]
    
    for user, canonical in test_cases:
        emb_user = model.encode(user)
        emb_canon = model.encode(canonical)
        similarity = float(emb_user @ emb_canon)
        
        status = "✅" if similarity > 0.6 else "❌"
        print(f"\n   {status} \"{user}\" → {similarity:.1%}")

if __name__ == '__main__':
    # Ищем scenarios.json
    json_paths = [
        '../scenarios.json',
        '../../scenarios.json',
        'scenarios.json',
        os.path.join(SCRIPT_DIR, '..', 'scenarios.json'),
        os.path.join(SCRIPT_DIR, '..', '..', 'scenarios.json'),
    ]
    
    json_path = None
    for path in json_paths:
        if os.path.exists(path):
            json_path = path
            break
    
    if not json_path:
        print(f"❌ scenarios.json не найден!")
        exit(1)
    
    print(f"📖 Загружаем сценарии из: {json_path}")
    
    positive, negative = load_training_data(json_path)
    print(f"📖 Положительных пар: {len(positive)}")
    print(f"📖 Отрицательных пар: {len(negative)}")
    
    examples = prepare_training_examples(positive, negative)
    model = train_model(examples, MODEL_DIR)
    
    if model:
        test_model(model)
        print("\n✅ ОБУЧЕНИЕ ЗАВЕРШЕНО!")
        print(f"📁 Модель в папке: {MODEL_DIR}")
    else:
        print("\n❌ ОБУЧЕНИЕ НЕ ВЫПОЛНЕНО")