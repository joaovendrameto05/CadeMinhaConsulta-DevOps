# Validação do backend .NET

## Ambiente e verificação

- Visual Studio Community 2026 com workload web, SDK .NET 11.0.100 e runtime ASP.NET Core 10.0.15. A solução continua compilando com alvo `net10.0`.
- MongoDB Community 8.0.38 portátil, extraído do ZIP oficial e verificado pelo SHA-256 publicado pelo fornecedor. O processo roda em loopback e usa `.local/mongodb/data`. O banco de desenvolvimento está em User Secrets. Compass, mongosh e serviço do Windows são dispensáveis para este roteiro.
- O processo MongoDB foi desligado e iniciado por um PowerShell novo. O script reutilizou os dados e recusou uma segunda instância na mesma porta. O arquivo User Secrets permaneceu idêntico byte a byte. A verificação ocorreu sem reiniciar o Windows e sem configurar início automático na entrada da sessão.
- Após a renomeação, `dotnet restore`, `dotnet build` e `dotnet test` passaram na nova solução, com **47 aprovados, zero falhas e zero pulados**. O build teve zero avisos. Na auditoria anterior, a consulta do NuGet não reportou pacotes diretos ou transitivos vulneráveis nas fontes configuradas. As dependências não mudaram nesta revisão.
- Dois testes xUnit usaram um MongoDB real. Um testou regras de lembrete, e o outro testou notificações com consultas fictícias fornecidas pela suíte. Os demais cenários cobrem unidades e HTTP em memória, assinaturas JWT, BSON e SQL gerado.
- A API foi iniciada pelo perfil `CadeMinhaConsulta.Api`. O Kestrel recebeu requisições HTTP em todos os endpoints. O MongoDB foi desligado normalmente e reiniciado no mesmo diretório de dados. Após reiniciar o servidor e o Kestrel, conferimos o estado de leitura de uma notificação e a resposta original de uma criação de regra.
- Na revisão da renomeação, o Visual Studio abriu `CadeMinhaConsulta.slnx`. No Kestrel, Swagger exibiu o novo título, o diagnóstico retornou 401 sem token e 204 com JWT de desenvolvimento, a leitura MongoDB retornou 200 e os User Secrets permaneceram idênticos. A prontidão retornou 503 com MongoDB saudável e Oracle não saudável. O login Oracle retornou `ORA-01017` no check de leitura. Essa revisão não implantou objetos Oracle.
- Após a atualização da senha, a implantação do DBv3 no Oracle FIAP foi concluída em `RM561450`. No Kestrel, `/health/live` e `/health/ready` retornaram 200 com Oracle e MongoDB saudáveis.
- No fechamento, `restore`, `build` e `test` passaram novamente na solução. Resultado de **47 testes aprovados, zero falhas e zero pulados**, com as duas integrações MongoDB reais. O build teve zero avisos e zero erros.
- Na checagem final pelo Kestrel, liveness, readiness e Swagger retornaram 200. Com uma regra fictícia em MongoDB isolado, confirmei CRUD, erros 400/401/403/404, replay com 201 idêntico, conflito 409 e 429 com `Retry-After`. `GET /api/reminder-rules` retornou 200 com JWT `CIDADAO` e o corpo da regra foi conferido. A chamada foi feita por HTTP no backend e ainda precisa ser repetida no aplicativo React Native.
- Na preparação Mobile, o cliente HTTP Node.js confirmou `GET /api/reminder-rules` 200 com JWT `CIDADAO` e os campos esperados da regra. O mesmo token de demonstração recebeu 401 em `Production`. A suíte xUnit passou novamente com 47 aprovados. A [tela de exemplo](examples/ReminderRulesScreen.tsx) e os passos para obter a evidência no app estão no [contrato Mobile](Contrato-Mobile.md).

## Evidência por critério FIAP

| Requisito | Implementação | Teste executado | Evidência | Pendência | Risco de desconto |
|---|---|---|---|---|---|
| Minimal API RESTful e Swagger · 20 pts | CRUD de regras, notificações, validação, respostas HTTP e OpenAPI para rotas públicas e autenticadas. | Kestrel com Oracle e MongoDB reais e asserts no OpenAPI. | CRUD e notificações em HTTP, incluindo GET que coincide com SELECTs por titular e PATCH 204. Rotas de saúde públicas no Swagger. | Executar o fluxo no React Native. | Médio se a demonstração ficar restrita ao Swagger. |
| JWT e autorização · 20 pts | Assinatura RS256, emissor, audiência, expiração, perfis e isolamento por titular. | JWT de desenvolvimento no Kestrel e testes xUnit com JWT assinado. | 401 anônimo, 403 para escrita com CIDADAO e escrita com GESTOR. | Confirmar contrato de produção com o Java e testar um JWT real. | Médio para a integração Mobile autenticada. |
| Idempotência · 15 pts | ID estável e gravação atômica no MongoDB para regras e notificações. | Concorrência nos dois testes com MongoDB real. Replay via Kestrel antes e depois de reiniciar o host e o banco. | Um único registro por chave, corpo e Location preservados, 409 para conteúdo diferente. | Nenhuma no cenário validado. | Baixo. |
| Rate Limiting · 15 pts | Cota configurável por identidade e `Retry-After`. | Excesso de chamadas no Kestrel e teste de isolamento por usuário. | 429 com `Retry-After`. Chamadas anônimas retornaram 401. | Nenhuma no cenário validado. | Baixo. |
| xUnit · 20 pts | Testes unitários, HTTP em memória e integrações com MongoDB. | `dotnet test` com banco real dedicado. Um teste HTTP independente comparou Oracle real com a API. | 47 aprovados, zero falhas e zero pulados, incluindo dois cenários reais MongoDB. O xUnit de notificações usa consultas fornecidas pela suíte. | O cenário com Oracle FIAP foi executado separadamente no Kestrel. | Baixo. |
| Health Checks · 10 pts | Liveness público e readiness com estado separado Oracle/MongoDB. Qualquer falha ou degradação retorna 503. | Kestrel com ambos os bancos reais. Auditorias anteriores testaram falha e recuperação do MongoDB e configuração Oracle inválida. | Liveness 200 e readiness 200 com ambos `Healthy`. O readiness 503 também foi observado com dependência indisponível. | Manter ambos os bancos acessíveis durante a apresentação. | Baixo se as dependências estiverem disponíveis na demonstração. |

## Oracle → .NET → MongoDB

O DBv3 tem 13 tabelas ativas. O Kestrel retornou, respectivamente, uma e duas notificações de dois titulares fictícios. Os IDs das consultas, os status e as datas coincidiram com SELECTs diretos no Oracle filtrados pelos mesmos titulares.

- GET sem autenticação retornou 401. O GET repetido com o primeiro titular retornou o mesmo corpo e os mesmos IDs.
- PATCH com o token do outro titular retornou 404 e preservou o estado da notificação. Dois PATCHs com o token do titular retornaram 204.
- Uma consulta direta ao MongoDB isolado confirmou três documentos, um único registro por notificação, índice `_id_` e o estado de leitura persistido. Os User Secrets permaneceram idênticos durante o teste.

Veja os [metadados e a preparação do DBv3](Oracle-DBv3.md). O teste de ponta a ponta usou os dois bancos reais e JWTs de desenvolvimento.

## Demonstração

O [roteiro da banca](Roteiro-Demonstracao.md) relaciona cada critério aos arquivos implementados, aos testes existentes e aos passos para demonstrá-lo. Também prepara respostas sobre arquitetura, autenticação, EF Core e idempotência. O [contrato React Native](Contrato-Mobile.md) inclui URL por ambiente e uma tela de exemplo para listar regras.

A evidência de consumo pelo aplicativo depende da execução no projeto da equipe Mobile. A verificação HTTP no Kestrel comprova o comportamento da API .NET. A chamada e a captura da tela React Native continuam pendentes.

## Conferência da entrega

- A branch `main` contém os commits locais, sem remoto. A revisão do conjunto versionável procurou credenciais em texto, tokens, chaves privadas, binários e dados locais. A entrega ainda precisa de um repositório GitHub com README acessível ao professor.
- O ZIP preliminar, na pasta local de entrega ignorada pelo Git, contém o TXT conferido dos cinco integrantes e um arquivo de link sinalizado como pendente. Substitua o aviso pelo link verdadeiro antes de enviar. Inclua o PowerPoint se houver. O PDF pede RM, nome completo e turma no TXT e indica desconto de 100 pontos para arquivo incompleto, corrompido ou sem identificação. A equipe precisa revisar a declaração de uso de IA no README.
- O ZIP técnico anterior à renomeação foi extraído e compilado. Na pasta de extração, o Controle de Aplicativos do Windows bloqueou o carregamento da DLL da API com `0x800711C7`. Os 47 testes aprovados correspondem à execução no workspace principal.
- Exclua do ZIP as pastas `.local`, `.vs`, `bin`, `obj` e quaisquer arquivos com segredos. O envio pelo Portal FIAP está previsto até **19/11/2026 às 23h55**.
