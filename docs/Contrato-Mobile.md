# Contrato REST para React Native

## URL base e autenticação

| Ambiente | URL base |
|---|---|
| Swagger e PowerShell no Windows | `http://localhost:5050` |
| Emulador Android oficial na mesma máquina | `http://10.0.2.2:5050` no build Android de desenvolvimento. |
| Android físico por USB | `http://127.0.0.1:5050` após executar `adb reverse tcp:5050 tcp:5050` com o aparelho de teste conectado. |
| Dispositivo físico pela rede e avaliação | URL HTTPS atribuída à API e acessível ao dispositivo. O endereço definitivo depende do ambiente da equipe. |

`10.0.2.2` aponta para o loopback do computador no Android Emulator. O perfil `CadeMinhaConsulta.Api` abre o Kestrel na porta local 5050. Para Android físico por USB, confirme o aparelho com `adb devices` antes de configurar o encaminhamento. Se o build bloquear HTTP local, restrinja qualquer configuração de cleartext à variante de depuração usada com dados fictícios. Para uso pela rede, publique apenas a API em um endpoint HTTPS com certificado aceito pelo aparelho. O MongoDB continua em loopback. [Rede do Android Emulator](https://developer.android.com/studio/run/emulator-networking-address) · [React Native em dispositivo](https://reactnative.dev/docs/running-on-device) · [Segurança de rede Android](https://developer.android.com/privacy-and-security/security-config)

O `fetch` nativo do React Native não depende de CORS. Se a equipe também executar uma versão Web no navegador, configure no ASP.NET Core apenas as origens necessárias, os métodos usados e o header `Authorization`. Exponha `Retry-After` nessa política caso a tela Web precise lê-lo. [Rede no React Native](https://reactnative.dev/docs/network) · [CORS no ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/cors?view=aspnetcore-10.0)

Envie `Authorization: Bearer <token>`. Para notificações, o JWT precisa de `sub` e da claim assinada configurada em `Jwt:OracleUserIdClaim`, que representa o `ID_USUARIO` do DBv3. Para ler regras, o token precisa de `CIDADAO` ou `GESTOR`. Escrever regras exige `GESTOR`. O script de desenvolvimento emite tokens somente para uma demonstração local. A configuração de produção depende de confirmar o contrato com Java.

## Chamadas e respostas

| Operação | Resultado |
|---|---|
| `GET /api/notifications/me` | `200` com array de notificações do titular. O array pode estar vazio. Exige Oracle e MongoDB. |
| `PATCH /api/notifications/{id}/read` | `204` sem corpo. Pode ser repetido. `404` se a notificação estiver ausente ou pertencer a outro titular. |
| `GET /api/reminder-rules` | `200` com array de regras. A operação depende apenas do MongoDB. |

```typescript
const headers = {
  Authorization: `Bearer ${accessToken}`,
  Accept: 'application/json',
};

const notifications = await fetch(`${baseUrl}/api/notifications/me`, {headers});
if (notifications.ok) {
  const items = await notifications.json();
  // Renderize o array, inclusive o estado vazio.
}

const read = await fetch(
  `${baseUrl}/api/notifications/${encodeURIComponent(notificationId)}/read`,
  {method: 'PATCH', headers},
);
if (read.status === 204) {
  // Atualize isRead na tela.
}

```

Trate `401` como sessão ausente ou inválida, `403` como acesso negado e `503` como indisponibilidade temporária. Para `429`, use o valor de `Retry-After` em segundos quando presente. Na resposta de notificações, `observedAtUtc` é o instante em que a API observou o status. `appointmentAt` preserva o horário do Oracle sem offset até a confirmação do fuso pelo ambiente.

## Exemplo de tela para GET /api/reminder-rules

Passe a URL da tabela e o JWT da sessão ao componente. Em uma demonstração local, gere o token `CIDADAO` pelo [roteiro de desenvolvimento](Roteiro-Demonstracao.md). O issuer de demonstração só é aceito em `Development`. Passe o token em tempo de execução, sem salvá-lo no código, no build ou em logs.

O exemplo [ReminderRulesScreen.tsx](examples/ReminderRulesScreen.tsx) pode ser copiado para o app. Registre a tela na navegação existente e passe a URL .NET da configuração do app e o token da sessão:

```tsx
import {ReminderRulesScreen} from './ReminderRulesScreen';

// Dentro da rota ou tela da equipe.
<ReminderRulesScreen
  baseUrl={dotnetBaseUrl}
  accessToken={session.accessToken}
/>;
```

A tela mostra carregamento, lista vazia, dados e erro. O JWT segue no header `Authorization`. Em desenvolvimento, use uma sessão de teste fornecida em tempo de execução. O exemplo recebe o token por propriedade e não altera o login existente.

### Validação HTTP disponível

Em 17/11/2026, um cliente Node.js chamou o Kestrel real. O GET sem JWT retornou 401. Com um JWT `CIDADAO` de desenvolvimento, `GET /api/reminder-rules` retornou 200 e incluiu a regra fictícia criada para o teste. Oracle e MongoDB passaram no readiness. O mesmo JWT de demonstração retornou 401 quando o Kestrel foi iniciado em `Production`.

O projeto React Native é mantido pela equipe Mobile e está fora deste workspace. A tela fica disponível como referência para integrar ao app existente. A execução e a captura no aplicativo são responsabilidade da equipe Mobile e continuam pendentes.

## Evidência Mobile pendente

1. No projeto React Native da equipe, integrar o exemplo à navegação e à sessão existentes. Configurar a URL .NET de acordo com o alvo de execução.
2. Para uma demonstração independente do Oracle, criar uma regra pelo Swagger como `GESTOR` em um MongoDB de teste. Na tela React Native, chamar `GET /api/reminder-rules` com um token `CIDADAO` de teste e mostrar o dado retornado e o status 200.
3. Executar a tela em um emulador ou aparelho Android. Registrar o título da regra exibido e o método, a rota e o status 200 no inspetor de rede ou no log do Kestrel durante a chamada. Mantenha tokens e headers de autenticação fora da captura.
