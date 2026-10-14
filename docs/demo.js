const sample=[{start:0,end:4200,source:'You can review every line before export.',translated:'导出前，你可以逐句检查字幕。'},{start:4200,end:8200,source:'The demo never calls an AI service.',translated:'这个演示不会调用任何 AI 服务。'}];
let playing=false;let timer;
const rows=document.querySelector('#rows');const status=document.querySelector('#status');
function render(){rows.innerHTML=sample.map((item,index)=>`<div class="subtitle"><label>English<textarea data-field="source" data-index="${index}">${item.source}</textarea></label><label>Chinese<textarea data-field="translated" data-index="${index}">${item.translated}</textarea></label></div>`).join('')}
function time(ms){const seconds=Math.floor(ms/1000);return `00:${String(seconds).padStart(2,'0')}`}
function tick(){const seek=document.querySelector('#seek');const value=Number(seek.value)+100;seek.value=String(value>10000?0:value);document.querySelector('#clock').value=time(Number(seek.value));if(playing)timer=requestAnimationFrame(tick)}
document.querySelector('#playButton').onclick=()=>{playing=!playing;document.querySelector('#playButton').textContent=playing?'Pause sample':'Play sample';if(playing)timer=requestAnimationFrame(tick);else cancelAnimationFrame(timer)};
document.querySelector('#seek').oninput=event=>document.querySelector('#clock').value=time(Number(event.target.value));
document.querySelector('#saveButton').onclick=()=>{document.querySelectorAll('textarea').forEach(input=>sample[input.dataset.index][input.dataset.field]=input.value);document.querySelector('#revision').textContent='Revision 4';status.textContent='Saved locally in this demo.'};
document.querySelector('#downloadButton').onclick=()=>{const body=sample.map((item,index)=>`${index+1}\n00:00:00,000 --> 00:00:04,200\n${item.translated}\n`).join('\n');const link=document.createElement('a');link.href=URL.createObjectURL(new Blob([body],{type:'text/plain'}));link.download='sample.zh.srt';link.click();URL.revokeObjectURL(link.href)};
render();
