# Cade Minha Consulta - Solucao CitizenTech (.NET 10, DevOps & Cloud Computing)

**FIAP - 2o Ano Analise e Desenvolvimento de Sistemas - Global Solution 2026/2 (CitizenTech)**  
**Disciplina:** DevOps Tools & Cloud Computing (Integrada com Advanced Business Development with .NET, Compliance QA & Tests e Mastering Relational and Non-Relational Database)  
**Grupo:** Celtics Tech

---

## 1. Identificacao da Equipe (Grupo Celtics Tech)

| Nome Completo do Integrante | RM | Turma | Responsabilidade no Projeto |
| :--- | :---: | :---: | :--- |
| Joao Victor Vendrameto | 563665 | 2TDSPV | Cloud & DevOps Engineer / Provisionamento IaC & Pipelines CI/CD |
| Gabriel Ambrosio Saraiva | 566552 | 2TDSPV | Scrum Master / Arquitetura Cloud & Banco de Dados |
| Vinicius Romaguera Cardozo | 562308 | 2TDSPX | Backend Developer / Modelagem Relacional e NoSQL |
| Nicolas de Oliveira Jacob | 564205 | 2TDSPX | Backend Developer / Integracao de Endpoints e Contratos |
| Yuri Fuzinatto Garzoli Barreto | 561450 | 2TDSPX | .NET Lead Developer / Seguranca JWT, Idempotencia & Testes xUnit |

* **Link da Organizacao no Azure DevOps:** `https://dev.azure.com/rm563365`
* **Link do Projeto Oficial DevOps:** `https://dev.azure.com/rm563365/CadeMinhaConsulta-DevOps`
* **Link Publico da API em Nuvem (Swagger UI no Azure ACI):** `http://cmc-api-rm563665.brazilsouth.azurecontainer.io:8080/swagger/index.html`
* **Endpoints de Saude (Health Checks em Nuvem):**
  * Liveness Probe: `http://cmc-api-rm563665.brazilsouth.azurecontainer.io:8080/health/live`
  * Readiness Probe: `http://cmc-api-rm563665.brazilsouth.azurecontainer.io:8080/health/ready`

---

## 2. Conceito da SolucaoProposta (CitizenTech - ODS 3 e ODS 10)

A solucao **Cade Minha Consulta** e uma plataforma **CitizenTech** desenvolvida para combater o absenteismo em consultas e exames especializados do Sistema Unico de Saude (SUS) e permitir o reaproveitamento automatico de vagas liberadas:

* **Problema Real:** O indice de faltas em consultas e exames especializados na rede publica varia entre 25% e 38%, motivado pelo desconhecimento do agendamento (61,2%), esquecimento (23,5%), dificuldades de deslocamento (29,0%) e cadastros telefonicos desatualizados (14,5%).
* **Solucao Tecnologica:** Backend em nuvem desenvolvido em **ASP.NET Core 10 Minimal API**, integrando persistencia poliglota em nuvem (**Oracle Database 19c Cloud** e **MongoDB 8.0 Container**) para gerenciar unidades de saude (UBS/AME com codigo CNES), especialidades e valores da tabela SUS/SIGTAP, regras de lembrete com controle estrito de idempotencia e notificacoes sincronizadas das consultas dos cidadaos.
* **Alinhamento com os ODS da ONU:** **ODS 3** (Saude e Bem-Estar) e **ODS 10** (Reducao das Desigualdades).

---

## 3. Desenho Macro da Arquitetura Cloud & Fluxo DevOps CI/CD

O desenho macro da arquitetura foi elaborado seguindo o padrao de topologia de nuvem (dividido nas camadas de Atores/IaC, Plataforma Azure DevOps, Nuvem Microsoft Azure `rg-cademinhaconsulta-gs` na regiao `Brazil South` e Nuvem Oracle Database Cloud FIAP), com o arquivo editavel do Draw.io versionado em `docs/arquitetura-macro-cloud.drawio` e a imagem renderizada em `docs/arquitetura-macro-cloud.png`:

![Desenho Macro da Arquitetura Cloud e DevOps](docs/arquitetura-macro-cloud.png)

### 3.1 Fluxo Numerado da Arquitetura (Etapas 1 a 11 do Diagrama)

1. **Planejamento no Azure Boards e Codificacao:** O desenvolvimento inicia com a criacao de uma Tarefa (*Work Item Task*) no **Azure Boards** (processo Scrum) e a implementacao do codigo C# (`.NET 10.0`), testes `xUnit`, `Dockerfile` e scripts de infraestrutura no VS Code.
2. **Versionamento Git e Politicas de Branch no Azure Repos:** Uma nova branch e criada a partir da Task. A branch principal `main` possui politicas ativas de protecao: revisor obrigatorio (minimo 1), autoaprovacao permitida para simulacao, vinculacao obrigatoria de Work Item e revisor padrao automatico (`RM563665`).
3. **Acionamento da Pipeline de Build (CI):** Apos a aprovacao e conclusao do *Merge* via *Pull Request* na branch `main`, a pipeline declarativa `/azure-pipeline.yml` e acionada automaticamente em agente Linux (`ubuntu-latest`).
4. **Compilacao, Testes Automatizados xUnit e Push para o ACR:** A Pipeline de CI instala o SDK do `.NET 10.0`, restaura pacotes NuGet, compila a solucao em modo `Release`, executa automaticamente os **47 testes automatizados xUnit** (publicando o relatorio na aba *Tests*) e realiza o `docker build` e `docker push` da imagem `cademinhaconsulta-api:latest` para o **Azure Container Registry (`acrcademinhaconsultarm563665.azurecr.io`)**.
5. **Publicacao de Artefatos e Gatilho Continuo de Release:** A Pipeline de CI empacota a aplicacao junto com as pastas `/scripts` e `/dockerfiles` no artefato `drop`, disparando automaticamente o gatilho de implantacao continua (*Continuous Deployment Trigger*).
6. **Deploy Automatico pela Pipeline de Release (CD):** A pipeline `CadeMinhaConsulta-Release-CD` autentica na assinatura Azure por meio da Service Connection `Azure-Gabriel-GS` e executa a tarefa **Azure CLI**, provisionando/atualizando o container da API no **Azure Container Instances (ACI)** e injetando as variaveis de ambiente e strings de conexao secretas.
7. **Pull da Imagem Containerizada (ACR para ACI):** O servico **Azure Container Instances (`aci-api-cmc-rm563665`)** baixa a imagem atualizada diretamente do registro privado `acrcademinhaconsultarm563665.azurecr.io`.
8. **Provisionamento de Infraestrutura como Codigo (IaC):** O script `/scripts/provisionar-infra.azcli` cria via Azure CLI o grupo de recursos `rg-cademinhaconsulta-gs`, o registro ACR e as instancias de container no Azure, enquanto o script `/scripts/script-bd.sql` estrutura os objetos DDL no banco Oracle.
9. **Persistencia NoSQL em Nuvem (MongoDB 8.0 no Azure ACI):** A API `.NET 10` comunica-se na porta TCP `27017` com o container `aci-mongodb-cmc-rm563665` (`cmc-mongo-rm563665.brazilsouth.azurecontainer.io`), persistindo documentos nas colecoes `reminder_rules` (com chave de idempotencia SHA-256) e `notifications`.
10. **Persistencia Relacional em Nuvem (Oracle Database Cloud FIAP):** A API `.NET 10` conecta-se via driver gerenciado `ODP.NET Core` / `Entity Framework Core` na porta TCP `1521` ao banco `oracle.fiap.com.br` (`SID: ORCL`, Schema `RM563665`), executando o CRUD completo nas tabelas relacionais **`TB_CMC_UNIDADE`** e **`TB_CMC_ESPECIALIDADE`** e consultando agendamentos em **`TB_GS_CONSULTA`**, com comprovacao de persistencia via comando `SELECT` direto no Oracle SQL Developer.
11. **Consumo pelos Clientes em Nuvem:** O aplicativo Mobile React Native, os gestores de unidades de saude e a interface interativa do **Swagger UI** consomem a API publicamente na porta `8080` (`http://cmc-api-rm563665.brazilsouth.azurecontainer.io:8080/swagger/index.html`).

### 3.2 Legenda dos Recursos Provisionados em Nuvem

| Recurso / Servico | Nome / Identificador em Nuvem | Tipo / Tecnologia | Regiao / Host |
| :--- | :--- | :--- | :--- |
| **Resource Group** | `rg-cademinhaconsulta-gs` | Grupo Logico de Recursos Azure | `Brazil South` |
| **Container Registry (ACR)** | `acrcademinhaconsultarm563665` | Registro Privado Docker (`Basic`) | `acrcademinhaconsultarm563665.azurecr.io` |
| **Container da API (ACI)** | `aci-api-cmc-rm563665` | ASP.NET Core 10 Minimal API (Linux, 1 vCPU, 1.5 GB) | `cmc-api-rm563665.brazilsouth.azurecontainer.io:8080` |
| **Container NoSQL (ACI)** | `aci-mongodb-cmc-rm563665` | MongoDB 8.0 Community Container (Linux) | `cmc-mongo-rm563665.brazilsouth.azurecontainer.io:27017` |
| **Banco Relacional (PaaS)** | `ORCL` (Schema `RM563665`) | Oracle Database 19c Cloud | `oracle.fiap.com.br:1521` |

---

## 4. Estrutura de Diretorios e Arquivos Obrigatorios do Repositorio

```text
CadeMinhaConsulta-DevOps/
├── azure-pipeline.yml                         # Pipeline YAML de CI na raiz (Requisito 13)
├── CadeMinhaConsulta.slnx                     # Arquivo de Solucao .NET 10.0
├── README.md                                  # Documentacao da solucao, arquitetura e JSONs de CRUD
├── .gitignore                                 # Filtro de arquivos temporarios, binarios e segredos locais
├── dockerfiles/                               # Pasta obrigatoria de Dockerfiles (Requisito 12)
│   └── Dockerfile                             # Multi-stage build (.NET 10.0 SDK e ASP.NET Core 10.0 Runtime)
├── docs/                                      # Documentacao tecnica complementar e diagramas
│   ├── arquitetura-macro-cloud.png            # Imagem do Desenho Macro da Arquitetura Cloud e CI/CD
│   ├── arquitetura-macro-cloud.drawio         # Arquivo fonte editavel do diagrama no Draw.io
│   ├── Arquitetura.md
│   ├── Contrato-Mobile.md
│   ├── Oracle-DBv3.md
│   ├── Roteiro-Demonstracao.md
│   └── Validacao-DotNet.md
├── scripts/                                   # Pasta obrigatoria de scripts de banco e nuvem (Requisitos 09, 10 e 11)
│   ├── script-bd.sql                          # DDL das tabelas Oracle, INSERTs iniciais e SELECTs de validacao
│   ├── provisionar-infra.azcli                # Script Azure CLI para provisionamento de RG, ACR e Containers ACI
│   └── crud-tabelas.json                      # Payloads JSON estruturados para execucao dos CRUDs
├── src/
│   └── CadeMinhaConsulta.Api/                 # Projeto principal da API (.NET 10.0)
│       ├── CadeMinhaConsulta.Api.csproj
│       ├── Program.cs                         # Inicializacao, DI, Swagger, Rate Limiting, JWT, HealthChecks e Rotas
│       ├── Features/
│       │   ├── OracleCrud/
│       │   │   └── OracleCrudEndpoints.cs     # CRUD completo nas 2 tabelas Oracle (TB_CMC_UNIDADE e TB_CMC_ESPECIALIDADE)
│       │   ├── Notifications/
│       │   │   ├── Notification.cs
│       │   │   └── NotificationEndpoints.cs   # Sincronizacao de notificacoes Oracle -> MongoDB
│       │   └── ReminderRules/
│       │       ├── ReminderRule.cs
│       │       └── ReminderRuleEndpoints.cs   # CRUD idempotente de regras de lembrete no MongoDB
│       └── Infrastructure/
│           ├── DatabaseHealthChecks.cs
│           ├── JwtAuthentication.cs
│           ├── MongoNotificationStore.cs
│           ├── MongoReminderRuleStore.cs
│           ├── OraclePatientAppointments.cs
│           ├── OracleReadDbContext.cs
│           └── SwaggerAuthorizationFilter.cs
└── tests/
    └── CadeMinhaConsulta.InfrastructureTests/ # Suite com 47 testes automatizados xUnit executados no CI
        ├── ApiInfrastructureTests.cs
        ├── NotificationEndpointTests.cs
        ├── NotificationProjectionTests.cs
        ├── OracleMappingTests.cs
        └── ReminderRuleTests.cs
```

---

## 5. Payloads JSON para Execucao Completa do CRUD (Duas Tabelas Oracle + MongoDB)

Em conformidade com os **Requisitos 14, 15 e 19** da disciplina de DevOps Tools & Cloud Computing, todas as operacoes de **Create, Read, Update e Delete (CRUD)** sao executadas em nuvem sobre **duas tabelas relacionais no banco Oracle (`TB_CMC_UNIDADE` e `TB_CMC_ESPECIALIDADE`)** e comprovadas diretamente no banco por meio de instrucoes **`SELECT`** no Oracle SQL Developer.

### 5.1 Tabela 1 (Oracle Cloud): `TB_CMC_UNIDADE` — Endpoint Base: `/api/unidades`

#### 1) CREATE (`POST /api/unidades`)
* **Payload JSON de Entrada:**
```json
{
  "nome": "AME Vila Mariana",
  "cnes": "2089123",
  "cidade": "Sao Paulo",
  "uf": "SP"
}
```
* **Resposta HTTP Esperada (`201 Created`):**
```json
{
  "id": 2,
  "nome": "AME Vila Mariana",
  "cnes": "2089123",
  "cidade": "Sao Paulo",
  "uf": "SP",
  "criadoEm": "2026-11-18T18:33:54.7311398Z"
}
```

#### 2) READ (`GET /api/unidades` e `GET /api/unidades/2`)
* **Resposta HTTP Esperada (`200 OK`):**
```json
[
  {
    "id": 1,
    "nome": "AME Maria Zelia",
    "cnes": "2077485",
    "cidade": "Sao Paulo",
    "uf": "SP",
    "criadoEm": "2026-11-18T15:29:46"
  },
  {
    "id": 2,
    "nome": "AME Vila Mariana",
    "cnes": "2089123",
    "cidade": "Sao Paulo",
    "uf": "SP",
    "criadoEm": "2026-11-18T18:33:54"
  }
]
```

#### 3) UPDATE (`PUT /api/unidades/2`)
* **Payload JSON de Entrada:**
```json
{
  "nome": "AME Vila Mariana - Unidade Integrada SUS",
  "cnes": "2089123",
  "cidade": "Sao Paulo",
  "uf": "SP"
}
```
* **Resposta HTTP Esperada (`200 OK`):**
```json
{
  "id": 2,
  "nome": "AME Vila Mariana - Unidade Integrada SUS",
  "cnes": "2089123",
  "cidade": "Sao Paulo",
  "uf": "SP",
  "criadoEm": "2026-11-18T18:36:41.7245416Z"
}
```

#### 4) DELETE (`DELETE /api/unidades/2`)
* **Resposta HTTP Esperada:** `204 No Content`.

#### 5) Query SQL de Comprovacao de Persistencia Diretamente no Banco Oracle
```sql
SELECT ID_UNIDADE, NM_UNIDADE, CD_CNES, NM_CIDADE, SG_UF, DT_CRIACAO
FROM TB_CMC_UNIDADE
ORDER BY ID_UNIDADE;
```

---

### 5.2 Tabela 2 (Oracle Cloud): `TB_CMC_ESPECIALIDADE` — Endpoint Base: `/api/especialidades`

#### 1) CREATE (`POST /api/especialidades`)
* **Payload JSON de Entrada:**
```json
{
  "nome": "Ortopedia Adulto",
  "codigoSigtap": "0301010072",
  "valorSigtap": 10.79,
  "idUnidade": 1
}
```
* **Resposta HTTP Esperada (`201 Created`):**
```json
{
  "id": 2,
  "nome": "Ortopedia Adulto",
  "codigoSigtap": "0301010072",
  "valorSigtap": 10.79,
  "idUnidade": 1,
  "criadoEm": "2026-11-18T18:40:12.1120451Z"
}
```

#### 2) READ (`GET /api/especialidades` e `GET /api/especialidades/2`)
* **Resposta HTTP Esperada (`200 OK`):**
```json
[
  {
    "id": 1,
    "nome": "Cardiologia Clinica",
    "codigoSigtap": "0301010072",
    "valorSigtap": 10.79,
    "idUnidade": 1,
    "criadoEm": "2026-11-18T15:29:46"
  },
  {
    "id": 2,
    "nome": "Ortopedia Adulto",
    "codigoSigtap": "0301010072",
    "valorSigtap": 10.79,
    "idUnidade": 1,
    "criadoEm": "2026-11-18T18:40:12"
  }
]
```

#### 3) UPDATE (`PUT /api/especialidades/2`)
* **Payload JSON de Entrada:**
```json
{
  "nome": "Ortopedia e Traumatologia Especializada",
  "codigoSigtap": "0301010072",
  "valorSigtap": 90.36,
  "idUnidade": 1
}
```
* **Resposta HTTP Esperada (`200 OK`):**
```json
{
  "id": 2,
  "nome": "Ortopedia e Traumatologia Especializada",
  "codigoSigtap": "0301010072",
  "valorSigtap": 90.36,
  "idUnidade": 1,
  "criadoEm": "2026-11-18T18:42:05.5182910Z"
}
```

#### 4) DELETE (`DELETE /api/especialidades/2`)
* **Resposta HTTP Esperada:** `204 No Content`.

#### 5) Query SQL de Comprovacao de Persistencia Diretamente no Banco Oracle
```sql
SELECT ID_ESPECIALIDADE, NM_ESPECIALIDADE, CD_SIGTAP, VL_SIGTAP, ID_UNIDADE, DT_CRIACAO
FROM TB_CMC_ESPECIALIDADE
ORDER BY ID_ESPECIALIDADE;
```

---

### 5.3 Colecao NoSQL (MongoDB 8.0 no Azure ACI): `reminder_rules` — Endpoint Base: `/api/reminder-rules`

*(Protegido por autenticacao JWT RS256 com papel `GESTOR` e cabecalho obrigatorio `Idempotency-Key` no `POST`)*

* **CREATE (`POST /api/reminder-rules` com Header `Idempotency-Key: regra-sus-48h`):**
```json
{
  "name": "Lembrete de Consulta 48h",
  "daysBefore": 2,
  "message": "Confirme sua consulta especializada no aplicativo Cade Minha Consulta.",
  "isActive": true
}
```
* **UPDATE (`PUT /api/reminder-rules/{id}`):**
```json
{
  "name": "Lembrete Urgente 24h SUS",
  "daysBefore": 1,
  "message": "Sua consulta ocorre amanha. Confirme presenca ou libere a vaga para o proximo cidadao da fila.",
  "isActive": true
}
```
* **READ (`GET /api/reminder-rules`)** e **DELETE (`DELETE /api/reminder-rules/{id}`)**.

---

## 6. Variaveis de Ambiente nas Pipelines de Build (CI) e Release (CD)

Em atendimento ao **Requisito 15** e blindagem contra a **Penalidade 16**, nenhuma credencial ou senha encontra-se exposta no codigo-fonte. Todas as configuracoes sensiveis sao gerenciadas como variaveis de ambiente nas Pipelines do Azure DevOps com criptografia (*Keep this value secret*):

| Escopo da Variavel | Nome da Variavel | Valor / Finalidade |
| :--- | :--- | :--- |
| **Build (CI) & Release (CD)** | `ACR_LOGIN_SERVER` | `acrcademinhaconsultarm563665.azurecr.io` |
| **Build (CI) & Release (CD)** | `ACR_USERNAME` | `acrcademinhaconsultarm563665` |
| **Build (CI) & Release (CD)** | `ACR_PASSWORD` | `********` (Chave secreta do Azure Container Registry) |
| **Release (CD)** | `RESOURCE_GROUP` | `rg-cademinhaconsulta-gs` |
| **Release (CD)** | `ACI_API_NAME` | `aci-api-cmc-rm563665` |
| **Release (CD)** | `DNS_API` | `cmc-api-rm563665` |
| **Release (CD)** | `ORACLE_SCHEMA` | `RM563665` |
| **Release (CD)** | `ORACLE_CONNECTION_STRING` | `********` (String de conexao ODP.NET para `oracle.fiap.com.br:1521/ORCL`) |
| **Container Runtime (ACI)** | `ConnectionStrings__Mongo` | `mongodb://cmc-mongo-rm563665.brazilsouth.azurecontainer.io:27017` |
| **Container Runtime (ACI)** | `Mongo__Database` | `cademinhaconsulta_prod` |
| **Container Runtime (ACI)** | `ASPNETCORE_ENVIRONMENT` | `Development` (Habilita a documentacao Swagger UI na instancia de nuvem) |

---

## 7. Comandos de Compilacao e Execucao dos Testes Automatizados (xUnit)

```powershell
dotnet restore CadeMinhaConsulta.slnx
dotnet build CadeMinhaConsulta.slnx --configuration Release --no-restore
dotnet test CadeMinhaConsulta.slnx --configuration Release --no-build --logger trx
```