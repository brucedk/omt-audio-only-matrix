import { useState, useEffect, useCallback } from 'react';
import { Settings, Activity, MonitorSpeaker, Mic } from 'lucide-react';
import './index.css';

// Mock types
type OmtSender = string;
type ChannelRowType = {
  id: number;
  selectedOmtSource: string;
  selectedOutput: string;
};

type AudioOutput = {
  id: string;
  name: string;
};

type BroadcastRowType = {
  id: string;
  deviceId: string;
  streamName: string;
};

function App() {
  const [senders, setSenders] = useState<OmtSender[]>([]);
  const [outputs, setOutputs] = useState<AudioOutput[]>([]);
  const [inputs, setInputs] = useState<AudioOutput[]>([]);
  const [broadcasts, setBroadcasts] = useState<BroadcastRowType[]>([]);
  
  // 8 Channels State Initialize
  const [channels, setChannels] = useState<ChannelRowType[]>(
    Array.from({ length: 8 }, (_, i) => ({
      id: i + 1,
      selectedOmtSource: '',
      selectedOutput: ''
    }))
  );

  const fetchState = useCallback(async () => {
    try {
      const sendersRes = await fetch('/api/routing/senders');
      if (sendersRes.ok) {
        const sendersData = await sendersRes.json();
        setSenders(sendersData);
      }
      
      const outputsRes = await fetch('/api/routing/outputs');
      if (outputsRes.ok) {
        const outputsData = await outputsRes.json();
        setOutputs(outputsData);
      }
      
      const inputsRes = await fetch('/api/routing/inputs');
      if (inputsRes.ok) {
        const inputsData = await inputsRes.json();
        setInputs(inputsData);
      }
      
      const configRes = await fetch('/api/routing/config');
      if (configRes.ok) {
        const configData: Record<string, string> = await configRes.json();
        setChannels(prev => prev.map(ch => {
          const dataStr = configData[ch.id.toString()];
          let source = '';
          let output = '';
          if (dataStr) {
            try {
              const parsed = JSON.parse(dataStr);
              source = parsed.source || '';
              output = parsed.output || '';
            } catch {
              source = dataStr;
            }
          }
          return {
            ...ch,
            selectedOmtSource: source,
            selectedOutput: output
          };
        }));
      }

      const broadcastRes = await fetch('/api/routing/broadcast-config');
      if (broadcastRes.ok) {
        const broadcastData: Record<string, string> = await broadcastRes.json();
        setBroadcasts(prev => {
          // Create a new array and clone objects to avoid mutation
          const merged = prev.filter(b => b.deviceId === '' || Object.keys(broadcastData).includes(b.deviceId)).map(b => ({...b}));
          
          Object.keys(broadcastData).forEach(k => {
             const existing = merged.find(b => b.deviceId === k);
             if (!existing) {
                // Only add from backend if it doesn't exist locally. 
                // We do NOT overwrite local streamNames, so typing isn't interrupted by polling!
                merged.push({ id: Math.random().toString(), deviceId: k, streamName: broadcastData[k] });
             }
          });
          return merged;
        });
      }
    } catch (err) {
      console.error("Failed to fetch state from backend:", err);
    }
  }, []);

  useEffect(() => {
    fetchState();
    const interval = setInterval(fetchState, 5000); // refresh every 5 seconds
    return () => clearInterval(interval);
  }, [fetchState]);

  const updateBackendConfig = async (newChannels: ChannelRowType[]) => {
    const configData: Record<string, string> = {};
    newChannels.forEach(ch => {
      if (ch.selectedOmtSource || ch.selectedOutput) {
        configData[ch.id.toString()] = JSON.stringify({
          source: ch.selectedOmtSource,
          output: ch.selectedOutput
        });
      }
    });
    
    try {
      await fetch('/api/routing/config', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json'
        },
        body: JSON.stringify(configData)
      });
    } catch (err) {
      console.error("Failed to save config to backend:", err);
    }
  };

  const updateBroadcastConfig = async (newBroadcasts: BroadcastRowType[]) => {
    const configData: Record<string, string> = {};
    newBroadcasts.forEach(b => {
      if (b.deviceId && b.streamName) {
        configData[b.deviceId] = b.streamName;
      }
    });
    
    try {
      await fetch('/api/routing/broadcast-config', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json'
        },
        body: JSON.stringify(configData)
      });
    } catch (err) {
      console.error("Failed to save broadcast config to backend:", err);
    }
  };

  const saveBroadcasts = () => {
    setBroadcasts(prev => {
      updateBroadcastConfig(prev);
      return prev;
    });
  };

  const addBroadcast = () => {
    setBroadcasts(prev => [...prev, { id: Math.random().toString(), deviceId: '', streamName: 'My Stream ' + (prev.length + 1) }]);
  };

  const removeBroadcast = (id: string) => {
    setBroadcasts(prev => {
      const newBroadcasts = prev.filter(b => b.id !== id);
      updateBroadcastConfig(newBroadcasts);
      return newBroadcasts;
    });
  };

  const updateBroadcast = (id: string, field: 'deviceId' | 'streamName', value: string) => {
    setBroadcasts(prev => {
      const newBroadcasts = prev.map(b => b.id === id ? { ...b, [field]: value } : b);
      // Auto-saving here causes restarts on every keystroke. 
      // User must explicitly click Save for name changes.
      if (field === 'deviceId') {
         updateBroadcastConfig(newBroadcasts);
      }
      return newBroadcasts;
    });
  };

  const handleSourceChange = (id: number, source: string) => {
    setChannels(prev => {
      const newChannels = [...prev];
      const chIndex = newChannels.findIndex(c => c.id === id);
      newChannels[chIndex].selectedOmtSource = source;
      
      updateBackendConfig(newChannels);
      return newChannels;
    });
  };

  const handleOutputChange = (id: number, output: string) => {
    setChannels(prev => {
      const newChannels = [...prev];
      const chIndex = newChannels.findIndex(c => c.id === id);
      newChannels[chIndex].selectedOutput = output;
      
      updateBackendConfig(newChannels);
      return newChannels;
    });
  };

  return (
    <div className="app-container">
      <h1 className="title">OMT Audio Control Matrix</h1>
      
      <div className="dashboard-grid">
        
        {/* Left column: Discovered senders */}
        <div className="glass-panel">
          <div className="section-header">
             <Activity className="text-primary" /> Discovered Network Sources
          </div>
          <div style={{display:'flex', flexDirection:'column', gap:'0.8rem'}}>
            {senders.map(s => (
              <div key={s} style={{background:'rgba(59, 130, 246, 0.1)', padding:'1rem', borderRadius:'8px', display:'flex', alignItems:'center', gap:'1rem'}}>
                 <MonitorSpeaker size={20} color="#60a5fa" />
                 <div>
                   <div style={{fontWeight:600}}>{s}</div>
                   <div style={{fontSize:'0.8rem', color:'var(--text-muted)'}}>Active • Ready to route</div>
                 </div>
              </div>
            ))}
          </div>
        </div>

        {/* Right column: 8 Audio Driver Inputs routing */}
        <div className="glass-panel">
          <div className="section-header">
             <Settings className="text-primary" /> Audio Driver Routing (8 IN)
          </div>
          <div className="channel-container">
            {channels.map((ch) => (
               <div key={ch.id} className="channel-row">
                  <div className="channel-info">
                     <div className="channel-icon">
                        <Mic size={18} />
                     </div>
                     <div>
                        <div style={{fontWeight:600}}>
                           Channel {ch.id}
                        </div>
                     </div>
                  </div>
                  
                  <div style={{display:'flex', alignItems:'center', gap:'1rem'}}>
                     <select 
                        value={ch.selectedOmtSource} 
                        onChange={(e) => handleSourceChange(ch.id, e.target.value)}
                        style={{minWidth: '150px'}}
                     >
                        <option value="">-- No Source --</option>
                        {senders.map(s => <option key={s} value={s}>{s}</option>)}
                     </select>

                     <span style={{color: 'var(--text-muted)'}}>to</span>

                     <select 
                        value={ch.selectedOutput} 
                        onChange={(e) => handleOutputChange(ch.id, e.target.value)}
                        style={{minWidth: '150px'}}
                        title={outputs.find(o => o.id === ch.selectedOutput)?.name || ''}
                     >
                        <option value="">-- No Output --</option>
                        {outputs.map(out => <option key={out.id} value={out.id}>{out.name}</option>)}
                     </select>
                  </div>
               </div>
            ))}
          </div>
        </div>
        
        {/* Third column: Broadcast Local Audio */}
        <div className="glass-panel">
          <div className="section-header">
             <Settings className="text-primary" /> Local Broadcasts (OMT Send)
          </div>
          <div className="channel-container">
            {broadcasts.map((b, index) => (
               <div key={b.id} className="channel-row" style={{flexDirection: 'column', alignItems: 'stretch', gap: '0.5rem'}}>
                  <div style={{display:'flex', justifyContent: 'space-between', alignItems: 'center'}}>
                     <div className="channel-info">
                        <div className="channel-icon">
                           <Mic size={18} />
                        </div>
                        <div style={{fontWeight:600}}>
                           Broadcast {index + 1}
                        </div>
                     </div>
                     <button onClick={() => removeBroadcast(b.id)} style={{background: 'transparent', border: 'none', color: '#ef4444', cursor: 'pointer', padding: '0.5rem'}}>
                        Remove
                     </button>
                  </div>
                  
                  <div style={{display:'flex', flexDirection:'column', gap:'0.5rem', marginTop: '0.5rem'}}>
                     <select 
                        value={b.deviceId} 
                        onChange={(e) => updateBroadcast(b.id, 'deviceId', e.target.value)}
                        style={{width: '100%'}}
                     >
                        <option value="">-- Select Local Input Device --</option>
                        {inputs.map(inp => <option key={inp.id} value={inp.id}>{inp.name}</option>)}
                     </select>
                     <div style={{display:'flex', gap:'0.5rem'}}>
                        <input 
                           type="text" 
                           placeholder="Stream Name"
                           value={b.streamName}
                           onChange={(e) => updateBroadcast(b.id, 'streamName', e.target.value)}
                           style={{flex: 1, padding: '0.5rem', background: 'rgba(0,0,0,0.3)', color: 'white', border: '1px solid rgba(255,255,255,0.1)', borderRadius: '6px'}}
                        />
                        <button 
                           onClick={saveBroadcasts} 
                           style={{background: 'var(--primary)', color: 'white', border: 'none', padding: '0.5rem 1rem', borderRadius: '6px', cursor: 'pointer', fontWeight: 600}}
                        >
                           Save
                        </button>
                     </div>
                  </div>
               </div>
            ))}
            
            <button 
              onClick={addBroadcast}
              style={{padding: '1rem', background: 'rgba(59, 130, 246, 0.2)', border: '1px dashed #3b82f6', color: '#3b82f6', borderRadius: '8px', cursor: 'pointer', marginTop: '1rem', fontWeight: 600}}
            >
              + Add Broadcast
            </button>
          </div>
        </div>
        
      </div>
    </div>
  );
}

export default App;
