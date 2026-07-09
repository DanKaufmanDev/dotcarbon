import { useState } from 'react'
import { invoke } from '@dotcarbon/api'

function App() {
    const [name, setName] = useState('')
    const [greeting, setGreeting] = useState('')

    async function greet() {
        setGreeting(await invoke('app:greet', { name }))
    }

    return (
        <main style={{
            fontFamily: 'system-ui',
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            justifyContent: 'center',
            height: '100vh',
            background: '#0f0f0f',
            color: 'white',
            margin: 0,
        }}>
            <h1 style={{ color: '#6366f1' }}>⚡ {{APP_NAME}}</h1>
            <p style={{ color: '#888' }}>Running on Carbon + React</p>

            <form
                onSubmit={(e) => { e.preventDefault(); greet() }}
                style={{ display: 'flex', gap: 8, marginTop: 24 }}
            >
                <input
                    value={name}
                    onChange={(e) => setName(e.currentTarget.value)}
                    placeholder="Enter a name..."
                    style={{
                        padding: '10px 14px',
                        borderRadius: 8,
                        border: '1px solid #333',
                        background: '#1a1a1a',
                        color: 'white',
                        fontSize: 15,
                    }}
                />
                <button type="submit" style={{
                    padding: '10px 24px',
                    background: '#6366f1',
                    color: 'white',
                    border: 'none',
                    borderRadius: 8,
                    fontSize: 15,
                    cursor: 'pointer',
                }}>
                    Greet
                </button>
            </form>

            <p style={{ color: '#6366f1', marginTop: 16, minHeight: 22 }}>{greeting}</p>

            <p style={{ color: '#555', marginTop: 24, fontSize: 13 }}>
                Edit <code>ui/src/App.tsx</code> and <code>src-carbon/Program.cs</code>
            </p>
        </main>
    )
}

export default App
