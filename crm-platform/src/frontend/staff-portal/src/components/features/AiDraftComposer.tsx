import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useRunSyncAi } from '../../hooks/useAiJobs';
import { Button } from '../ui';
import type { CapabilityType } from '../../types';

interface AiDraftComposerProps {
  entityType: string;
  entityId: string;
  capability?: CapabilityType;
  onInsert?: (draft: string) => void;
}

export function AiDraftComposer({
  entityType,
  entityId,
  capability = 'DraftGeneration',
  onInsert,
}: AiDraftComposerProps) {
  const { t } = useTranslation();
  const [prompt, setPrompt] = useState('');
  const [draft, setDraft] = useState('');
  const runSync = useRunSyncAi();

  const handleGenerate = () => {
    if (!prompt.trim()) return;
    runSync.mutate(
      { capability, entityType, entityId, inputPayload: JSON.stringify({ prompt }) },
      {
        onSuccess: (result) => {
          setDraft(result.outputText ?? '');
        },
      },
    );
  };

  const handleInsert = () => {
    if (onInsert && draft) {
      onInsert(draft);
      setDraft('');
      setPrompt('');
    }
  };

  return (
    <div className="rounded-lg border border-primary-200 bg-primary-50 p-4 space-y-3">
      <h3 className="text-sm font-semibold text-primary-800">{t('ai.draftComposer.title')}</h3>

      <textarea
        value={prompt}
        onChange={(e) => { setPrompt(e.target.value); }}
        placeholder={t('ai.draftComposer.placeholder')}
        rows={3}
        className="block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-primary-500 focus:outline-none focus:ring-1 focus:ring-primary-500"
      />

      <Button
        size="sm"
        onClick={handleGenerate}
        loading={runSync.isPending}
        disabled={!prompt.trim()}
      >
        {runSync.isPending ? t('ai.draftComposer.generating') : t('ai.draftComposer.generate')}
      </Button>

      {draft && (
        <div className="rounded-md border border-gray-200 bg-white p-3 text-sm text-gray-700 whitespace-pre-line">
          {draft}
          <div className="mt-3 flex justify-end">
            <Button size="sm" variant="secondary" onClick={handleInsert}>
              {t('ai.draftComposer.insertDraft')}
            </Button>
          </div>
        </div>
      )}
    </div>
  );
}
