const form = document.querySelector('#form');
const select = form.elements.servico;
const message = document.querySelector('#message');
const estimate = document.querySelector('#estimate');
let prices = {};
let pendingKey = null;
form.addEventListener('input', () => { pendingKey = null; });
fetch('/api/servicos').then(r => r.json()).then(data => {
  prices = data;
  Object.keys(data).forEach(name => select.add(new Option(name, name)));
}).catch(() => { message.className = 'error'; message.textContent = 'Não foi possível carregar os serviços. Recarregue a página.'; });
function updateEstimate() {
  const price = prices[select.value] * Number(form.elements.quantidade.value);
  estimate.textContent = Number.isFinite(price) && price > 0 ? price.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' }) : 'Selecione um serviço';
}
select.addEventListener('change', updateEstimate);
form.elements.quantidade.addEventListener('input', updateEstimate);
form.addEventListener('submit', async event => {
  event.preventDefault();
  if (!form.reportValidity()) return;
  const button = form.querySelector('button');
  button.disabled = true; message.textContent = 'Enviando…'; message.className = '';
  const data = Object.fromEntries(new FormData(form));
  data.quantidade = Number(data.quantidade); data.consentimento = !!data.consentimento;
  const key = pendingKey ?? crypto.randomUUID();
  pendingKey = key;
  try {
    const response = await fetch('/api/solicitacoes', { method: 'POST', headers: { 'Content-Type': 'application/json', 'Idempotency-Key': key }, body: JSON.stringify(data) });
    const result = await response.json();
    if (!response.ok) throw new Error(result.errors ? Object.values(result.errors).flat().join(' ') : result.erro || 'Não foi possível enviar.');
    message.className = 'success'; message.textContent = `Solicitação recebida. Protocolo: ${result.id}. Status: ${result.status}.`;
    form.reset(); updateEstimate(); pendingKey = null;
  } catch (error) { message.className = 'error'; message.textContent = error.message || 'Erro de conexão. Tente novamente.'; }
  finally { button.disabled = false; }
});
