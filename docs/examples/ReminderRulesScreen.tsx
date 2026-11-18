import React, {useEffect, useState} from 'react';
import {ActivityIndicator, Button, FlatList, Text, View} from 'react-native';

type Rule = {
  id: string;
  name: string;
  daysBefore: number;
  message: string;
  isActive: boolean;
};

type ScreenState =
  | {kind: 'loading'}
  | {kind: 'error'; message: string}
  | {kind: 'ready'; rules: Rule[]};

function describeError(response: Response): string {
  switch (response.status) {
    case 401:
      return 'Sessão inválida ou expirada. Entre novamente.';
    case 403:
      return 'Seu perfil não pode consultar as regras.';
    case 429: {
      const seconds = response.headers.get('Retry-After');
      return seconds
        ? 'Muitas tentativas. Aguarde ' + seconds + ' s.'
        : 'Muitas tentativas. Aguarde antes de tentar novamente.';
    }
    case 503:
      return 'Serviço temporariamente indisponível.';
    default:
      return 'Falha ao consultar as regras (HTTP ' + response.status + ').';
  }
}

export function ReminderRulesScreen({
  baseUrl,
  accessToken,
}: {
  baseUrl: string;
  accessToken: string;
}) {
  const [state, setState] = useState<ScreenState>({kind: 'loading'});
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    if (!accessToken) {
      setState({kind: 'error', message: 'Entre para consultar as regras.'});
      return;
    }

    const request = new AbortController();
    const endpoint = baseUrl.replace(/\/+$/, '') + '/api/reminder-rules';
    setState({kind: 'loading'});

    async function load() {
      try {
        const response = await fetch(endpoint, {
          headers: {
            Authorization: 'Bearer ' + accessToken,
            Accept: 'application/json',
          },
          signal: request.signal,
        });
        const result: ScreenState = response.ok
          ? {kind: 'ready', rules: (await response.json()) as Rule[]}
          : {kind: 'error', message: describeError(response)};
        if (!request.signal.aborted) setState(result);
      } catch {
        if (!request.signal.aborted) {
          setState({kind: 'error', message: 'Falha de rede ao consultar as regras.'});
        }
      }
    }

    void load();
    return () => request.abort();
  }, [baseUrl, accessToken, attempt]);

  return (
    <View style={{flex: 1, padding: 16}}>
      <Text>Regras de lembrete</Text>
      <Button
        title="Atualizar"
        onPress={() => setAttempt(value => value + 1)}
        disabled={state.kind === 'loading'}
      />
      {state.kind === 'loading' && <ActivityIndicator />}
      {state.kind === 'error' && <Text>{state.message}</Text>}
      {state.kind === 'ready' && (
        <FlatList
          data={state.rules}
          keyExtractor={rule => rule.id}
          ListEmptyComponent={<Text>Nenhuma regra cadastrada.</Text>}
          renderItem={({item}) => (
            <View style={{marginTop: 12}}>
              <Text>{item.name}</Text>
              <Text>{item.daysBefore} dia(s) antes da consulta</Text>
              <Text>{item.message}</Text>
              <Text>{item.isActive ? 'Ativa' : 'Inativa'}</Text>
            </View>
          )}
        />
      )}
    </View>
  );
}
