const form = document.querySelector('#form');
const select = form.elements.servico;
const message = document.querySelector('#message');
const estimate = document.querySelector('#estimate');
const estimateBasis = document.querySelector('#estimate-basis');
const submitButton = document.querySelector('#submit-button');
const fieldNames = ['nome', 'email', 'telefone', 'servico', 'quantidade', 'descricao', 'consentimento'];
let prices = {};
let pendingKey = null;

const money = value => Number(value).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });

function fieldMessage(field) {
  const value = field.type === 'checkbox' ? field.checked : field.value.trim();
  if (field.required && !value) return field.name === 'consentimento' ? 'Confirme o aviso para continuar.' : 'Preencha este campo.';
  if (field.name === 'nome' && value.length < 2) return 'Informe pelo menos 2 caracteres.';
  if (field.name === 'descricao' && value.length < 10) return 'Descreva o projeto com pelo menos 10 caracteres.';
  if (field.name === 'email' && field.validity.typeMismatch) return 'Informe um e-mail válido.';
  if (field.name === 'quantidade' && (field.validity.rangeUnderflow || field.validity.rangeOverflow || field.validity.badInput || field.validity.stepMismatch)) return 'Use um número inteiro de 1 a 20.';
  if (field.validity.tooLong) return `Use no máximo ${field.maxLength} caracteres.`;
  return '';
}

function showFieldError(field, text) {
  const target = document.querySelector(`#error-${field.name}`);
  if (target) target.textContent = text;
  field.setAttribute('aria-invalid', text ? 'true' : 'false');
}

function updateEstimate() {
  const unit = prices[select.value];
  const quantity = Number(form.elements.quantidade.value);
  const total = unit * quantity;
  estimate.textContent = Number.isFinite(total) && total > 0 ? money(total) : 'Selecione um serviço';
  estimateBasis.textContent = unit && quantity > 0 ? `${money(unit)} por unidade × ${quantity}` : 'Selecione um serviço para ver o cálculo';
}

async function loadServices() {
  select.disabled = true;
  try {
    const response = await fetch('/api/servicos');
    if (!response.ok) throw new Error();
    prices = await response.json();
    select.replaceChildren(new Option('Selecione uma opção', ''), ...Object.keys(prices).map(name => new Option(name, name)));
    select.disabled = false;
    if (!Object.keys(prices).length) throw new Error();
  } catch {
    message.className = 'error';
    message.textContent = 'Não foi possível carregar os serviços. Recarregue a página para tentar novamente.';
  }
}

form.addEventListener('input', event => {
  pendingKey = null;
  if (event.target.name && fieldNames.includes(event.target.name)) {
    event.target.dataset.touched = 'true';
    showFieldError(event.target, fieldMessage(event.target));
  }
});
form.addEventListener('change', event => {
  if (event.target.name && fieldNames.includes(event.target.name)) showFieldError(event.target, fieldMessage(event.target));
});
select.addEventListener('change', updateEstimate);
form.elements.quantidade.addEventListener('input', updateEstimate);

form.addEventListener('submit', async event => {
  event.preventDefault();
  if (submitButton.disabled) return;
  const invalid = fieldNames.map(name => form.elements[name]).filter(field => {
    const error = fieldMessage(field);
    showFieldError(field, error);
    return !!error;
  });
  if (invalid.length) {
    message.className = 'error'; message.textContent = 'Revise os campos destacados antes de enviar.';
    invalid[0].focus(); return;
  }
  submitButton.disabled = true;
  submitButton.textContent = 'Enviando solicitação…';
  form.setAttribute('aria-busy', 'true');
  message.className = ''; message.textContent = 'Registrando seu pedido…';
  const data = Object.fromEntries(new FormData(form));
  data.quantidade = Number(data.quantidade); data.consentimento = !!data.consentimento;
  const key = pendingKey ?? crypto.randomUUID();
  pendingKey = key;
  try {
    const response = await fetch('/api/solicitacoes', { method: 'POST', headers: { 'Content-Type': 'application/json', 'Idempotency-Key': key }, body: JSON.stringify(data) });
    const result = await response.json();
    if (!response.ok) {
      if (result.errors) for (const [name, errors] of Object.entries(result.errors)) if (form.elements[name]) showFieldError(form.elements[name], errors[0]);
      throw new Error(result.errors ? 'Revise os campos destacados antes de enviar.' : result.erro || 'Não foi possível registrar o pedido.');
    }
    document.querySelector('#protocol').textContent = result.id;
    document.querySelector('#success-status').textContent = `Status inicial: ${result.status}.`;
    form.hidden = true;
    document.querySelector('#success').hidden = false;
    document.querySelector('#success').focus();
    form.reset(); updateEstimate(); pendingKey = null;
    fieldNames.forEach(name => showFieldError(form.elements[name], ''));
  } catch (error) {
    message.className = 'error'; message.textContent = error.message || 'Erro de conexão. Tente novamente.';
  } finally {
    form.removeAttribute('aria-busy');
    submitButton.disabled = false;
    submitButton.textContent = 'Enviar solicitação';
  }
});

document.querySelector('#new-request').addEventListener('click', () => {
  document.querySelector('#success').hidden = true;
  form.hidden = false;
  message.textContent = '';
  form.elements.nome.focus();
});

loadServices();
