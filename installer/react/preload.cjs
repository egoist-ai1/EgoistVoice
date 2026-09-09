const { contextBridge, ipcRenderer } = require('electron');
contextBridge.exposeInMainWorld('installer', {
  info: () => ipcRenderer.invoke('info'),
  chooseDirectory: () => ipcRenderer.invoke('choose-directory'),
  install: options => ipcRenderer.invoke('install', options),
  launch: () => ipcRenderer.invoke('launch'),
  close: () => ipcRenderer.send('close'),
  onProgress: callback => {
    const listener = (_event, state) => callback(state);
    ipcRenderer.on('progress', listener);
    return () => ipcRenderer.removeListener('progress', listener);
  },
});
