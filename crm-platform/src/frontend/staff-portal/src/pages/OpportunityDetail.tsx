import { useParams, useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useForm } from 'react-hook-form';
import { z } from 'zod';
import { zodResolver } from '@hookform/resolvers/zod';
import { useOpportunity, useUpdateOpportunity } from '../hooks/useOpportunities';
import { Input, Button, Badge, Spinner } from '../components/ui';
import type { UpdateOpportunityRequest, OpportunityStage } from '../types';

const STAGES: OpportunityStage[] = [
  'Prospecting', 'Qualification', 'Proposal', 'Negotiation', 'ClosedWon', 'ClosedLost',
];

const schema = z.object({
  name: z.string().min(1),
  stage: z.enum(['Prospecting', 'Qualification', 'Proposal', 'Negotiation', 'ClosedWon', 'ClosedLost']),
  amount: z.number().optional(),
  probability: z.number().min(0).max(100).optional(),
  closeDate: z.string().optional(),
});

type FormValues = z.infer<typeof schema>;

export function OpportunityDetail() {
  const { id } = useParams<{ id: string }>();
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { data: opp, isLoading } = useOpportunity(id ?? '');
  const updateOpp = useUpdateOpportunity(id ?? '');

  const { register, handleSubmit, formState: { errors, isDirty } } = useForm<FormValues>({
    resolver: zodResolver(schema),
    values: opp
      ? {
          name: opp.name,
          stage: opp.stage,
          amount: opp.amount,
          probability: opp.probability,
          closeDate: opp.closeDate ? opp.closeDate.split('T')[0] : '',
        }
      : undefined,
  });

  const onSubmit = (values: FormValues) => {
    const req: UpdateOpportunityRequest = {
      name: values.name,
      stage: values.stage,
      amount: values.amount,
      probability: values.probability,
      closeDate: values.closeDate,
    };
    updateOpp.mutate(req);
  };

  if (isLoading) {
    return <div className="flex h-64 items-center justify-center"><Spinner size="lg" /></div>;
  }
  if (!opp) {
    return <div className="p-6"><p className="text-gray-500">{t('errors.notFound')}</p></div>;
  }

  return (
    <div className="p-6 max-w-2xl mx-auto">
      <div className="mb-6 flex items-center gap-4">
        <button onClick={() => { void navigate('/opportunities'); }} className="text-sm text-gray-500 hover:text-gray-700">
          {`\u2190 ${t('opportunities.title')}`}
        </button>
        <h1 className="text-2xl font-bold text-gray-900">{opp.name}</h1>
        <Badge label={opp.stage} variant={opp.stage === 'ClosedWon' ? 'success' : opp.stage === 'ClosedLost' ? 'danger' : 'info'} />
      </div>

      <div className="rounded-lg border border-gray-200 bg-white p-6">
        <form onSubmit={(e) => { void handleSubmit(onSubmit)(e); }} className="space-y-4">
          <Input label={t('opportunities.name')} required {...register('name')} error={errors.name?.message} />
          <div>
            <label className="mb-1 block text-sm font-medium text-gray-700">{t('opportunities.stage')}</label>
            <select {...register('stage')} className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary-500">
              {STAGES.map((s) => (
                <option key={s} value={s}>{s}</option>
              ))}
            </select>
          </div>
          <Input label={t('opportunities.amount')} type="number" {...register('amount', { valueAsNumber: true })} error={errors.amount?.message} />
          <Input label={t('opportunities.probability')} type="number" min={0} max={100} {...register('probability', { valueAsNumber: true })} error={errors.probability?.message} />
          <Input label={t('opportunities.closeDate')} type="date" {...register('closeDate')} />
          <div className="flex justify-end">
            <Button type="submit" disabled={!isDirty} loading={updateOpp.isPending}>
              {t('common.save')}
            </Button>
          </div>
        </form>
      </div>
    </div>
  );
}
