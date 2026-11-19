export type WorkflowPending = { path: string; body: string };

async function database(): Promise<IDBDatabase | null> {
  if (typeof indexedDB === 'undefined') return null;
  return new Promise((resolve, reject) => {
    const request = indexedDB.open('grainledger-container-workflow-pending', 1);
    request.onupgradeneeded = () => { request.result.createObjectStore('requests'); };
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(Error('Could not open pending workflow storage.'));
    request.onblocked = () => reject(Error('Close older application tabs to open pending workflow storage.'));
  });
}

export async function readWorkflowPending(key: string): Promise<WorkflowPending | null> {
  const db = await database();
  if (!db) {
    const raw = sessionStorage.getItem(key); if (!raw) return null;
    try { return JSON.parse(raw) as WorkflowPending; } catch { return null; }
  }
  try {
    return await new Promise((resolve, reject) => {
      const request = db.transaction('requests', 'readonly').objectStore('requests').get(key);
      request.onsuccess = () => resolve((request.result as WorkflowPending | undefined) ?? null);
      request.onerror = () => reject(Error('Could not restore the pending workflow request.'));
    });
  } finally { db.close(); }
}

export async function storeWorkflowPending(key: string, value: WorkflowPending | null): Promise<void> {
  const db = await database();
  if (!db) { if (value) sessionStorage.setItem(key, JSON.stringify(value)); else sessionStorage.removeItem(key); return; }
  try {
    await new Promise<void>((resolve, reject) => {
      const tx = db.transaction('requests', 'readwrite'), store = tx.objectStore('requests');
      if (value) store.put(value, key); else store.delete(key);
      tx.oncomplete = () => resolve();
      tx.onerror = () => reject(Error('Could not retain the exact workflow request. No new request was sent.'));
      tx.onabort = () => reject(Error('Pending workflow storage was interrupted.'));
    });
  } finally { db.close(); }
}
