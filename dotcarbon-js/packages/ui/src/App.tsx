import { useState } from 'react'
import { fs } from '@dotcarbon/plugin-fs'
import type { DirEntry } from '@dotcarbon/plugin-fs'
import { dialog } from '@dotcarbon/plugin-dialog'

import { shell } from '@dotcarbon/plugin-shell'

import { carbonWindow } from '@dotcarbon/plugin-window'

import { notification } from '@dotcarbon/plugin-notification'
import { clipboard } from '@dotcarbon/plugin-clipboard'

async function testNotification() {
    await notification.send({
        title: 'Carbon',
        body: 'Notifications are working! 🎉',
    })
}

async function testClipboard() {
    await clipboard.writeText('Hello from Carbon!')
    const text = await clipboard.readText()
    alert(`Clipboard contains: ${text}`)
}

function Titlebar() {
    return (
        <div style={{
            height: 36,
            background: '#111',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            padding: '0 12px',
            userSelect: 'none',
            WebkitAppRegion: 'drag',
        } as React.CSSProperties}>
            <span style={{ fontSize: 13, color: '#888' }}>My Carbon App</span>
            <div style={{ display: 'flex', gap: 8, WebkitAppRegion: 'drag' } as React.CSSProperties}>
                <button onClick={() => carbonWindow.minimize()}>−</button>
                <button onClick={() => carbonWindow.maximize()}>□</button>
                <button onClick={() => carbonWindow.close()}>✕</button>
            </div>
        </div>
    )
}

async function runGit() {
    try {
        const output = await shell.run('git', ['log', '--oneline', '-5'], '/Users/myst/Documents/Developer/dotcarbon')
        alert(output)
    } catch (e: any) {
        alert('Error: ' + e.message)
    }
}

async function openGithub() {
    await shell.openUrl('https://github.com')
}

function App() {
    const [path, setPath] = useState('/tmp')
    const [entries, setEntries] = useState<DirEntry[]>([])
    const [fileContents, setFileContents] = useState<string | null>(null)
    const [error, setError] = useState<string | null>(null)

    async function openDir(dirPath: string) {
        try {
            setError(null)
            setFileContents(null)
            const result = await fs.readDir(dirPath)
            setEntries(result)
            setPath(dirPath)
        } catch (e: unknown) {
            setError(e instanceof Error ? e.message : String(e))
        }
    }

    async function openFile(filePath: string) {
        try {
            setError(null)
            const contents = await fs.readFile(filePath)
            setFileContents(contents)
        } catch (e: unknown) {
            setError(e instanceof Error ? e.message : String(e))
        }
    }

    async function pickAndRead() {
    const files = await dialog.openFile({
        title: 'Pick a text file',
        filters: ['*.txt', '*.md', '*.json']
    })

    if (!files || files.length === 0) return

    const contents = await fs.readFile(files[0])
    console.log(contents)
    alert(`Read ${files[0].split('/').pop()} — ${contents.length} chars`)
}

    function handleClick(entry: DirEntry) {
        if (entry.isDirectory) {
            openDir(entry.path)
        } else {
            openFile(entry.path)
        }
    }

    function goUp() {
        const parent = path.split('/').slice(0, -1).join('/') || '/'
        openDir(parent)
    }

    return (
        <div style={{ fontFamily: 'system-ui', padding: 24, background: '#1a1a1a', color: 'white', minHeight: '100vh' }}>
            <Titlebar />
            <button onClick={testNotification} style={{ marginRight: 8, padding: '4px 8px', background: '#444', color: 'white', border: 'none', borderRadius: 4, cursor: 'pointer' }}>
                Test Notification
            </button>
            <button onClick={testClipboard} style={{ marginRight: 8, padding: '4px 8px', background: '#444', color: 'white', border: 'none', borderRadius: 4, cursor: 'pointer' }}>
                Test Clipboard
            </button>
            <h2>⚡ Carbon File Explorer</h2>

            <div style={{ display: 'flex', gap: 8, marginBottom: 16 }}>
                <button onClick={goUp}>↑ Up</button>
                <input
                    value={path}
                    onChange={e => setPath(e.target.value)}
                    onKeyDown={e => e.key === 'Enter' && openDir(path)}
                    style={{ flex: 1, padding: '4px 8px', background: '#333', color: 'white', border: '1px solid #555', borderRadius: 4 }}
                />
                <button onClick={() => openDir(path)}>Go</button>
            </div>

            {error && (
                <p style={{ color: '#f87171' }}>Error: {error}</p>
            )}

            {fileContents !== null ? (
                <div>
                    <button onClick={() => setFileContents(null)}>← Back</button>
                    <pre style={{ marginTop: 16, background: '#111', padding: 16, borderRadius: 8, overflow: 'auto', maxHeight: 500 }}>
                        {fileContents}
                    </pre>
                    <button onClick={pickAndRead} style={{ marginLeft: 8, padding: '4px 8px', background: '#ff0000', color: 'white', border: 'none', borderRadius: 4, cursor: 'pointer' }}>
                                Pick & Read File
                    </button>
                    <button onClick={openGithub} style={{ marginLeft: 8, padding: '4px 8px', background: '#ff0000', color: 'white', border: 'none', borderRadius: 4, cursor: 'pointer' }}>
                                Open GitHub
                    </button>
                    <button onClick={runGit} style={{ marginLeft: 8, padding: '4px 8px', background: '#ff0000', color: 'white', border: 'none', borderRadius: 4, cursor: 'pointer' }}>
                                Run Git Log
                    </button>
                </div>
            ) : (

                <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
                    <button onClick={pickAndRead} style={{ marginLeft: 8, padding: '4px 8px', background: '#444', color: 'white', border: 'none', borderRadius: 4, cursor: 'pointer' }}>
                                Pick & Read File
                            </button>
                    <button onClick={openGithub} style={{ marginLeft: 8, padding: '4px 8px', background: '#ff0000', color: 'white', border: 'none', borderRadius: 4, cursor: 'pointer' }}>
                                Open GitHub
                    </button>
                    <button onClick={runGit} style={{ marginLeft: 8, padding: '4px 8px', background: '#ff0000', color: 'white', border: 'none', borderRadius: 4, cursor: 'pointer' }}>
                                Run Git Log
                    </button>
                    {entries.length === 0 && (
                        <p style={{ color: '#888' }}>Type a path and press Go to browse</p>
                    )}
                    {entries.map(entry => (
                        <div
                            key={entry.path}
                            onClick={() => handleClick(entry)}
                            style={{ padding: '8px 12px', background: '#2a2a2a', borderRadius: 6, cursor: 'pointer', display: 'flex', gap: 8, alignItems: 'center' }}
                        >
                            <span>{entry.isDirectory ? '📁' : '📄'}</span>
                            <span style={{ flex: 1 }}>{entry.name}</span>
                            {!entry.isDirectory && (
                                <span style={{ color: '#888', fontSize: 12 }}>
                                    {(entry.size / 1024).toFixed(1)}kb
                                </span>
                            )}
                            <button onClick={pickAndRead} style={{ marginLeft: 8, padding: '4px 8px', background: '#444', color: 'white', border: 'none', borderRadius: 4, cursor: 'pointer' }}>
                                Pick & Read File
                            </button>
                        </div>
                    ))}
                </div>
            )}
        </div>
    )
}

export default App