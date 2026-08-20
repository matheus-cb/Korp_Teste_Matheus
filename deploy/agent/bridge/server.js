// Ponte de inferencia para o Copiloto do NotaFlow.
//
// Esta ponte NAO fala MCP e NAO conhece o catalogo. Ela recebe um prompt,
// executa o Claude Code em modo -p e devolve o texto cru. Toda a inteligencia
// de dominio -- buscar produto, validar proveniencia, aplicar teto -- fica no
// Billing, que ja e dono da sessao MCP.
//
// Essa divisao resolve por construcao tres problemas da proposta original em
// docs/plano-agente-vps.md:
//   5.4  a ponte nunca recebe o INTERNAL_SERVICE_TOKEN, entao nao ganha o
//        poder de debitar estoque;
//   2.2  DiscoveredProductIds e preenchido pelo Billing a partir de resultados
//        MCP reais, e nao auto-declarado por quem implementa o provedor;
//   2.3  o teto de chamadas vive no Billing, valendo para qualquer provedor.
//
// Sem dependencia externa: so a stdlib do Node.
'use strict';

const http = require('node:http');
const { execFile } = require('node:child_process');
const { timingSafeEqual } = require('node:crypto');
const path = require('node:path');

const PORTA = Number(process.env.BRIDGE_PORT || 5099);
const HOST = process.env.BRIDGE_HOST || '127.0.0.1';
const SEGREDO = process.env.BRIDGE_SECRET || '';
const CLAUDE = process.env.CLAUDE_BIN || '/var/lib/nfagent/.local/bin/claude';
const MODELO = process.env.BRIDGE_MODEL || 'haiku';
const TIMEOUT_MS = Number(process.env.BRIDGE_TIMEOUT_MS || 90_000);
const MAX_PROMPT = 24_000;
// O CLI carrega .claude/settings.json e CLAUDE.md do diretorio onde roda. A
// ponte nao pode herdar configuracao de projeto nenhum: alem de imprevisivel,
// um repo com settings proprios muda o comportamento do modelo sem aviso.
const CWD = process.env.BRIDGE_CWD || __dirname;

if (!SEGREDO) {
    console.error('BRIDGE_SECRET e obrigatorio.');
    process.exit(1);
}

// O harness do Claude Code traz ferramentas de sistema. Nenhuma pode estar
// disponivel aqui (secao 5.1). Duas camadas: allowlist com um nome que nao
// existe, e negacao explicita de cada ferramenta embutida.
const FERRAMENTAS_NEGADAS = [
    'Bash', 'Edit', 'Write', 'Read', 'Glob', 'Grep', 'NotebookEdit',
    'WebFetch', 'WebSearch', 'Task', 'TodoWrite', 'Agent',
];

function segredoConfere(recebido) {
    if (typeof recebido !== 'string') return false;
    const a = Buffer.from(recebido);
    const b = Buffer.from(SEGREDO);
    if (a.length !== b.length) return false;
    return timingSafeEqual(a, b);
}

// Uma execucao por vez: 2 GB de RAM nao comportam varios harness simultaneos.
let ocupado = false;

function executar(prompt) {
    return new Promise((resolve, reject) => {
        // Windows: o npm instala `claude.cmd`, e execFile nao executa .cmd sem
        // shell. Rodar com shell colocaria o prompt numa linha de comando, o que
        // e porta aberta para injecao -- entao invocamos o wrapper .cjs com node.
        const usaNode = /\.(c?js)$/i.test(CLAUDE);
        const executavel = usaNode ? process.execPath : CLAUDE;
        const prefixo = usaNode ? [CLAUDE] : [];

        const args = [
            '-p', prompt,
            '--output-format', 'json',
            '--model', MODELO,
            '--no-session-persistence',
            '--allowedTools', 'NenhumaFerramentaPermitida',
            '--disallowedTools', ...FERRAMENTAS_NEGADAS,
        ];
        // O CLI espera dados em stdin por 3s antes de prosseguir e imprime um
        // aviso em stderr. Sem fechar o stdin, cada chamada custa 3s a mais e o
        // aviso polui a saida.
        const filho = execFile(executavel, [...prefixo, ...args], {
            cwd: CWD,
            timeout: TIMEOUT_MS,
            maxBuffer: 8 * 1024 * 1024,
            env: {
                PATH: process.env.PATH,
                HOME: process.env.HOME,
                CLAUDE_CODE_OAUTH_TOKEN: process.env.CLAUDE_CODE_OAUTH_TOKEN || '',
                ANTHROPIC_API_KEY: process.env.ANTHROPIC_API_KEY || '',
            },
        }, (erro, stdout, stderr) => {
            if (erro) {
                // NUNCA usar erro.message: o Node o monta com a linha de comando
                // inteira, e o prompt vai junto (INV-22). Só as ultimas linhas do
                // stderr, que carregam a mensagem do CLI, e o codigo de saida.
                const cauda = String(stderr || '').trim().split(/[\r\n]+/).slice(-3).join(' ').trim().slice(0, 300);
                return reject(new Error(
                    erro.killed ? 'timeout' : (cauda || `o CLI saiu com codigo ${erro.code ?? 'desconhecido'}`)));
            }
            resolve(stdout);
        });
        filho.stdin.end();
    });
}

function extrairTexto(saidaBruta) {
    // --output-format json devolve um envelope; o texto do modelo vem em
    // `result`. Se o formato mudar, cair para a saida crua e deixar o Billing
    // reprovar, em vez de adivinhar.
    try {
        const envelope = JSON.parse(saidaBruta);
        if (envelope.is_error) {
            const e = new Error(String(envelope.result || 'erro do harness').slice(0, 300));
            e.doHarness = true;
            throw e;
        }
        if (typeof envelope.result === 'string') return envelope.result;
    } catch (e) {
        if (e.doHarness) throw e;
        // formato inesperado: devolver cru e deixar o Billing reprovar
    }
    return saidaBruta;
}

const servidor = http.createServer((req, res) => {
    const responder = (status, corpo) => {
        const dados = JSON.stringify(corpo);
        res.writeHead(status, { 'content-type': 'application/json', 'content-length': Buffer.byteLength(dados) });
        res.end(dados);
    };

    if (req.method === 'GET' && req.url === '/health') return responder(200, { status: 'ok' });
    if (req.method !== 'POST' || req.url !== '/draft') return responder(404, { erro: 'rota desconhecida' });

    let corpo = '';
    let excedeu = false;
    req.on('data', (pedaco) => {
        corpo += pedaco;
        if (corpo.length > MAX_PROMPT * 2) { excedeu = true; req.destroy(); }
    });
    req.on('close', () => { if (excedeu) responder(413, { erro: 'requisicao grande demais' }); });

    req.on('end', async () => {
        if (excedeu) return;
        let pedido;
        try { pedido = JSON.parse(corpo); } catch { return responder(400, { erro: 'json invalido' }); }

        if (!segredoConfere(pedido.segredo)) return responder(401, { erro: 'nao autorizado' });
        if (typeof pedido.prompt !== 'string' || !pedido.prompt.trim()) return responder(400, { erro: 'prompt ausente' });
        if (pedido.prompt.length > MAX_PROMPT) return responder(413, { erro: 'prompt grande demais' });
        if (ocupado) return responder(429, { erro: 'ponte ocupada' });

        ocupado = true;
        const inicio = Date.now();
        try {
            const texto = extrairTexto(await executar(pedido.prompt));
            // INV-22: registrar duracao e tamanho, nunca o prompt nem a resposta.
            console.log(`draft ok em ${Date.now() - inicio}ms, ${texto.length} chars`);
            responder(200, { texto });
        } catch (e) {
            console.error(`draft falhou em ${Date.now() - inicio}ms: ${e.message}`);
            responder(502, { erro: e.message });
        } finally {
            ocupado = false;
        }
    });
});

// Nunca 0.0.0.0 (secao 5.5). O default e o loopback; na VPS o bind e o IP do
// gateway da bridge do Docker, que e um endereco privado do host, alcancavel
// pelos conteineres e bloqueado de fora pelo firewalld -- o container do
// Billing nao consegue falar com o loopback do host.
if (HOST === '0.0.0.0' || HOST === '::') {
    console.error('BRIDGE_HOST nao pode ser 0.0.0.0: a ponte precisa continuar fora do alcance externo.');
    process.exit(1);
}
servidor.listen(PORTA, HOST, () => console.log(`ponte ouvindo em ${HOST}:${PORTA}, modelo ${MODELO}`));
