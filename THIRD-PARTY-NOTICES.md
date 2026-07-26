# Сторонние компоненты

Egoist Voice распространяется вместе с речевыми моделями и нативными библиотеками сторонних
авторов. Ниже перечислено всё, что попадает в установщик или в сборку, с указанием лицензии.

Исходный код самого Egoist Voice лицензирован отдельно — см. `LICENSE`.

---

## Речевые модели (входят в установщик)

### GigaAM v3 — основной русский движок

- Автор: Salute Developers (СберБанк)
- Источник: https://github.com/salute-developers/GigaAM
- Веса в формате sherpa-onnx: https://huggingface.co/Smirnov75/GigaAM-v3-sherpa-onnx
- Лицензия: **MIT**

Установщик кладёт `gigaam_v3_e2e_rnnt_encoder_int8.onnx`, `..._decoder.onnx`, `..._joint.onnx` и
`..._tokens.txt` в `%LOCALAPPDATA%\EgoistVoice\Models\Speech`. При удалении приложения через
Windows они удаляются вместе с ним.

### Whisper large-v3-turbo — фолбэк для смешанной русско-английской речи

- Автор модели: OpenAI
- Формат GGML: https://github.com/ggml-org/whisper.cpp
- Лицензия: **MIT**

Установщик кладёт `ggml-large-v3-turbo-q5_0.bin` туда же.

---

## Библиотеки

| Компонент | Назначение | Лицензия | Источник |
|---|---|---|---|
| sherpa-onnx (`org.k2fsa.sherpa.onnx`) | рантайм GigaAM | Apache-2.0 | https://github.com/k2-fsa/sherpa-onnx |
| ONNX Runtime | исполнение ONNX-моделей | MIT | https://github.com/microsoft/onnxruntime |
| Whisper.net | биндинг whisper.cpp для .NET | MIT | https://github.com/sandrohanea/whisper.net |
| whisper.cpp | инференс Whisper | MIT | https://github.com/ggml-org/whisper.cpp |
| NAudio | захват звука | MIT | https://github.com/naudio/NAudio |
| .NET 8 | среда исполнения | MIT | https://github.com/dotnet/runtime |
| Inno Setup | сборка установщика | модифицированная BSD | https://jrsoftware.org/isinfo.php |

---

## NVIDIA CUDA Runtime

Установщик включает `cublas64_13.dll`, `cublasLt64_13.dll` и `cudart64_13.dll` — компоненты
NVIDIA CUDA Redistributable, необходимые для GPU-ускорения Whisper на видеокартах NVIDIA.

На них распространяется **NVIDIA CUDA Toolkit End User License Agreement**, включая приложение
«Distribution of the CUDA Redistributables»: https://docs.nvidia.com/cuda/eula/

Приложение работает и без них — при отсутствии совместимой видеокарты Whisper использует Vulkan
или CPU.

---

## Шрифты и иконки

Интерфейс использует системные шрифты Windows (`Segoe UI Variable`, `Segoe UI`). Они не входят в
установщик. Все иконки капсулы нарисованы векторно в коде приложения и не заимствованы.

---

## Тексты лицензий

Полные тексты MIT и Apache-2.0:

- MIT: https://opensource.org/license/mit
- Apache-2.0: https://www.apache.org/licenses/LICENSE-2.0

Каждый из перечисленных проектов публикует свой текст лицензии по ссылке в таблице выше; при
распространении бинарной сборки этот файл сопровождает её и служит требуемым уведомлением.
