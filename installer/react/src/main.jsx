import React, { useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import './style.css';

const preview = !window.installer;
const bridge = window.installer ?? {
  info: async () => ({ directory: 'C:\\Users\\User\\AppData\\Local\\Programs\\Egoist Voice Compact', version: '2.2.1-rc.3' }),
  chooseDirectory: async () => null,
  install: async () => ({ phase: 'success', progress: 100 }),
  close: () => {}, launch: () => {}, onProgress: () => () => {},
};
function Mark({ animated = false }) {
  return <span className={`mark ${animated ? 'animated' : ''}`} aria-hidden="true">
    {[14, 25, 36, 25, 14].map((height, i) => <i key={i} style={{ height, animationDelay: `${i * 90}ms` }} />)}
  </span>;
}
function Check({ children, checked, onChange }) {
  return <label className="option"><span>{children}</span><span className="switch">
    <input type="checkbox" checked={checked} onChange={e => onChange(e.target.checked)} />
    <span aria-hidden="true" className="switch-track"><span /></span>
  </span></label>;
}
function App() {
  const [info, setInfo] = useState({ directory: '', version: '' });
  const [autoStart, setAutoStart] = useState(true);
  const [desktop, setDesktop] = useState(true);
  const [state, setState] = useState({ phase: 'ready', progress: 0 });
  const busy = state.phase === 'installing';
  useEffect(() => {
    bridge.info().then(setInfo).catch(() => setState({ phase: 'error', message: 'Не удалось подготовить установку. Откройте установщик ещё раз.' }));
    const unsubscribe = bridge.onProgress(setState);
    if (preview) {
      const phase = new URLSearchParams(location.search).get('state');
      if (phase === 'installing') setState({ phase, progress: 62 });
      if (phase === 'success') setState({ phase, progress: 100 });
      if (phase === 'error') setState({ phase, message: 'Не удалось обновить открытые файлы. Закройте Voice через значок в трее и повторите установку.' });
    }
    return unsubscribe;
  }, []);
  async function install() {
    setState({ phase: 'installing', progress: 0 });
    try { setState(await bridge.install({ directory: info.directory, autoStart, desktop })); }
    catch { setState({ phase: 'error', message: 'Не удалось начать установку. Попробуйте ещё раз.' }); }
  }
  async function chooseDirectory() {
    const directory = await bridge.chooseDirectory();
    if (directory) setInfo(current => ({ ...current, directory }));
  }
  return <main className="window">
    <header className="titlebar"><span>EGOIST</span><button className="close" aria-label="Закрыть установщик" onClick={bridge.close} disabled={busy}>×</button></header>
    <section className="content">
      <div className="brand"><Mark animated={busy} /><div><h1>Egoist Voice</h1><span className="version">{info.version}</span></div><span className="offline"><i />Офлайн</span></div>
      {state.phase === 'ready' && <div className="scene">
        <h2>Один шаг до диктовки</h2><p className="subtitle">Приложение и русская модель уже внутри.</p>
        <div className="directory"><div><span className="field-label">Папка установки</span><p title={info.directory}>{info.directory || 'Подготовка…'}</p></div><button className="text-button" onClick={chooseDirectory}>Изменить</button></div>
        <div className="options"><Check checked={autoStart} onChange={setAutoStart}>Запускать вместе с Windows</Check><Check checked={desktop} onChange={setDesktop}>Ярлык на рабочем столе</Check></div>
      </div>}
      {busy && <div className="scene progress-scene" aria-live="polite">
        <h2>{state.progress < 1 ? 'Проверяем готовность' : 'Устанавливаем Voice'}</h2>
        <p className="subtitle">Копируем приложение и модель на ваш компьютер.</p>
        <div className="progress-label"><span>Установка</span><strong>{Math.min(99, state.progress || 0)}<small>%</small></strong></div>
        <div className={`progress-track ${state.progress < 1 ? 'indeterminate' : ''}`} role="progressbar" aria-label="Ход установки" aria-valuemin={0} aria-valuemax={100} aria-valuenow={state.progress || 0}><span style={{ transform: `scaleX(${Math.max(1, Math.min(99, state.progress || 0)) / 100})` }} /></div>
        <p className="quiet">Это окно закроется только после завершения.</p>
      </div>}
      {state.phase === 'success' && <div className="scene result-scene" aria-live="polite">
        <div className="result-icon">✓</div><h2>Готово к вашему голосу</h2><p className="subtitle">Voice установлен. Можно начинать диктовку.</p>
        {state.restartRequired && <p className="quiet">Для завершения обновления перезагрузите Windows.</p>}
      </div>}
      {state.phase === 'error' && <div className="scene result-scene error-scene" role="alert">
        <div className="result-icon">!</div><h2>Не удалось установить</h2><p className="subtitle error-message">{state.message}</p>
        {state.logPath && <details><summary>Где посмотреть подробности</summary><p>{state.logPath}</p></details>}
      </div>}
    </section>
    <footer>
      {state.phase === 'ready' && <><p className="footnote">Завершите диктовку: открытый Voice будет закрыт для обновления.</p><button className="primary" onClick={install} disabled={!info.directory}>Установить <span aria-hidden="true">↗</span></button></>}
      {busy && <button className="primary" disabled>Устанавливаем…</button>}
      {state.phase === 'success' && <button className="primary" onClick={state.restartRequired ? bridge.close : bridge.launch}>{state.restartRequired ? 'Закрыть' : 'Открыть Voice'} <span aria-hidden="true">↗</span></button>}
      {state.phase === 'error' && <button className="primary" onClick={() => setState({ phase: 'ready', progress: 0 })}>Вернуться и повторить</button>}
    </footer>
  </main>;
}
createRoot(document.getElementById('root')).render(<App />);
