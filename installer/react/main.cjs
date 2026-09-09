const { app, BrowserWindow, ipcMain, dialog } = require('electron');
const path = require('node:path');
const fs = require('node:fs');
const os = require('node:os');
const { spawn, execFile } = require('node:child_process');
const { createHash } = require('node:crypto');

const runtime = fs.mkdtempSync(path.join(os.tmpdir(), 'EgoistVoiceSetup-'));
app.setPath('userData', path.join(runtime, 'profile'));
app.setAppUserModelId('Egoist.Voice.Setup');
let window, busy = false, installedDirectory = null;
let directory = path.join(process.env.LOCALAPPDATA || app.getPath('appData'), 'Programs', 'Egoist Voice Compact');
const logPath = path.join(runtime, 'installation.log');
const statusPath = path.join(runtime, 'progress.txt');
const resources = app.isPackaged ? process.resourcesPath : process.env.EGOIST_INSTALLER_RESOURCES;

function trusted(event) {
  if (!window || event.sender !== window.webContents || event.senderFrame !== window.webContents.mainFrame)
    throw new Error('Invalid installer frame');
}
function state(value) { if (window && !window.isDestroyed()) window.webContents.send('progress', value); }
async function previousDirectory() {
  const key = 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{5F84E54F-BE2E-46BA-970C-D1A774D3D239}_is1';
  // Base64 avoids the console code page corrupting an existing Cyrillic install path.
  const command = `$value = Get-ItemPropertyValue -LiteralPath '${key}' -Name InstallLocation -ErrorAction Stop; [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($value))`;
  await new Promise(resolve => execFile(path.join(process.env.WINDIR, 'System32', 'WindowsPowerShell', 'v1.0', 'powershell.exe'),
    ['-NoProfile', '-NonInteractive', '-Command', command], { windowsHide: true, timeout: 2500 }, (error, output) => {
      const previous = !error && Buffer.from(output.trim(), 'base64').toString('utf8');
      if (previous && validDirectory(previous)) directory = path.normalize(previous);
      resolve();
    }));
}
function validDirectory(value) {
  return typeof value === 'string' && path.isAbsolute(value) && !/["\x00-\x1f]/.test(value)
    && path.parse(path.resolve(value)).root !== path.resolve(value);
}
async function verifyPayload(manifest) {
  for (const file of manifest.files) {
    if (path.basename(file.name) !== file.name || !/\.(exe|bin)$/i.test(file.name)) throw new Error('Invalid payload name');
    const target = path.join(resources, 'payload', file.name);
    if (fs.statSync(target).size !== file.bytes) throw new Error('Invalid payload size');
    const hash = createHash('sha256');
    for await (const chunk of fs.createReadStream(target)) hash.update(chunk);
    if (hash.digest('hex') !== file.sha256) throw new Error('Invalid payload hash');
  }
}
async function install(options) {
  if (busy || !options || !validDirectory(options.directory) || typeof options.autoStart !== 'boolean' || typeof options.desktop !== 'boolean')
    return { phase: 'error', message: 'Выберите папку установки и повторите.' };
  busy = true;
  directory = path.normalize(options.directory);
  window.setProgressBar(2);
  state({ phase: 'installing', progress: 0 });
  let timer;
  try {
    fs.writeFileSync(statusPath, '0');
    const manifest = JSON.parse(fs.readFileSync(path.join(resources, 'payload', 'payload.json'), 'utf8'));
    if (!manifest.files.some(file => file.name === manifest.launch) || path.basename(manifest.launch) !== manifest.launch)
      throw new Error('Invalid launch file');
    await verifyPayload(manifest);
    const args = ['/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CLOSEAPPLICATIONS', '/RESTARTEXITCODE=3010',
      `/DIR=${directory}`, `/LOG=${logPath}`, `/EGOIST_STATUS=${statusPath}`,
      `/EGOIST_AUTOSTART=${options.autoStart ? '1' : '0'}`, `/EGOIST_DESKTOP=${options.desktop ? '1' : '0'}`];
    const child = spawn(path.join(resources, 'payload', manifest.launch), args, {
      shell: false, windowsHide: true, stdio: 'ignore', cwd: path.join(resources, 'payload'),
    });
    timer = setInterval(() => {
      try {
        const progress = Math.max(0, Math.min(99, Number(fs.readFileSync(statusPath, 'utf8').trim()) || 0));
        state({ phase: 'installing', progress }); window.setProgressBar(progress / 100);
      } catch { /* Inno creates the progress file when installation starts. */ }
    }, 200);
    const code = await new Promise((resolve, reject) => { child.once('error', reject); child.once('exit', resolve); });
    if (code !== 0 && code !== 3010) {
      return { phase: 'error', message: `Установка не завершена. Закройте Voice через значок в трее, проверьте папку и свободное место, затем повторите.\nКод: ${code ?? 'неизвестен'}.`, logPath };
    }
    const executable = path.join(directory, 'Egoist.Voice.exe');
    if (!fs.existsSync(executable)) throw new Error('Installed executable is missing');
    installedDirectory = directory;
    return { phase: 'success', progress: 100, restartRequired: code === 3010 };
  } catch (error) {
    fs.appendFileSync(logPath, `\nInstaller: ${error.message}\n`);
    return { phase: 'error', message: 'Не удалось подготовить файлы установки. Проверьте свободное место и целостность установщика.', logPath };
  } finally {
    clearInterval(timer); busy = false; window.setProgressBar(-1);
  }
}

app.whenReady().then(async () => {
  await previousDirectory();
  window = new BrowserWindow({ width: 620, height: 550, useContentSize: true, resizable: false,
    maximizable: false, frame: false, roundedCorners: true, backgroundColor: '#09090c',
    title: 'Egoist Voice — установка', icon: path.join(__dirname, 'icon.ico'), show: false,
    webPreferences: { preload: path.join(__dirname, 'preload.cjs'), contextIsolation: true, sandbox: true, nodeIntegration: false, spellcheck: false, devTools: false },
  });
  window.removeMenu();
  window.webContents.setWindowOpenHandler(() => ({ action: 'deny' }));
  window.webContents.on('will-navigate', event => event.preventDefault());
  window.on('close', event => { if (busy) event.preventDefault(); });
  ipcMain.handle('info', event => { trusted(event); return { directory, version: app.getVersion() }; });
  ipcMain.handle('choose-directory', async event => {
    trusted(event); if (busy) return null;
    const result = await dialog.showOpenDialog(window, { title: 'Папка для Egoist Voice', defaultPath: directory,
      buttonLabel: 'Выбрать папку', properties: ['openDirectory', 'createDirectory'] });
    if (!result.canceled && validDirectory(result.filePaths[0])) directory = result.filePaths[0];
    return result.canceled ? null : directory;
  });
  ipcMain.handle('install', (event, options) => { trusted(event); return install(options); });
  ipcMain.handle('launch', async event => {
    trusted(event); if (!installedDirectory || busy) return;
    try {
      const child = spawn(path.join(installedDirectory, 'Egoist.Voice.exe'), [], { cwd: installedDirectory, detached: true, stdio: 'ignore' });
      await new Promise((resolve, reject) => { child.once('spawn', resolve); child.once('error', reject); });
      child.unref(); app.quit();
    } catch { state({ phase: 'error', message: 'Voice установлен, но не запустился. Откройте его через меню «Пуск».', logPath }); }
  });
  ipcMain.on('close', event => { trusted(event); if (!busy) app.quit(); });
  await window.loadFile(path.join(__dirname, 'dist', 'index.html'));
  window.show();
});
app.on('window-all-closed', () => app.quit());
