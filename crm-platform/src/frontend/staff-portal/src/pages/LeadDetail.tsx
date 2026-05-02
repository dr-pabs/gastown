import { useParams, useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useForm } from 'react-hook-form';
import { z } from 'zod';
import { zodResolver } from '@hookform/resolvers/zod';
import { useLead, useUpdateLead } from '../hooks/useLeads';
import { Input, Button, Badge, Spinner } from '../components/ui';
import { AiDraftComposer } from '../components/features/AiDraftComposer';
import type { UpdateLeadRequest } from '../types';

const updateLeadSchema = z.object({
  firstName: z.string().min(1),
  lastName: z.string().min(1),
  email: z.string().email(),
  phone: z.string().optional(),
  company: z.string().optional(),
  jobTitle: z.string().optional(),
});

type UpdateLeadFormValues = z.infer<typeof updateLeadSchema>;

export function LeadDetail() {
  const { id } = useParams<{ id: string }>();
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { data: lead, isLoading } = useLead(id ?? '');
  const updateLead = useUpdateLead(id ?? '');

  const { register, handleSubmit, formState: { errors, isDirty } } = useForm<UpdateLeadFormValues>({
    resolver: zodResolver(updateLeadSchema),
    values: lead
      ? {
          firstName: lead.firstName,
          lastName: lead.lastName,
          email: lead.email,
          phone: lead.phone ?? '',
          company: lead.company ?? '',
          jobTitle: lead.jobTitle ?? '',
        }
      : undefined,
  });

  const onSubmit = (values: UpdateLeadFormValues) => {
    const req: UpdateLeadRequest = values;
    updateLead.mutate(req);
  };

  if (isLoading) {
    return (
      <div className="flex h-64 items-center justify-center">
        <Spinner size="lg" />
      </div>
    );
  }

  if (!lead) {
    return (
      <div className="p-6">
        <p className="text-gray-500">{t('errors.notFound')}</p>
      </div>
    );
  }

  return (
    <div className="p-6 max-w-4xl mx-auto">
      <div className="mb-6 flex items-center gap-4">
        <button
          onClick={() => { void navigate('/leads'); }}
          className="text-sm text-gray-500 hover:text-gray-700"
        >
          ← {t('leads.title')}
        </button>
        <h1 className="text-2xl font-bold text-gray-900">
          {lead.firstName} {lead.lastName}
        </h1>
        <Badge label={lead.stage} variant="info" />
        {lead.score !== undefined && (
          <span className="text-sm text-gray-500">
            {t('leads.score')}: {lead.score}
          </span>
        )}
      </div>

      <div className="grid grid-cols-3 gap-6">
        <div className="col-span-2 space-y-6">
          <div className="rounded-lg border border-gray-200 bg-white p-6">
            <h2 className="mb-4 text-lg font-semibold">{t('common.details')}</h2>
            <form
              onSubmit={(e) => { void handleSubmit(onSubmit)(e); }}
              className="space-y-4"
            >
              <div className="grid grid-cols-2 gap-4">
                <Input
                  label={t('leads.firstName')}
                  required
                  {...register('firstName')}
                  error={errors.firstName?.message}
                />
                <Input
                  label={t('leads.lastName')}
                  required
                  {...register('lastName')}
                  error={errors.lastName?.message}
                />
              </div>
              <Input
                label={t('common.email')}
                type="email"
                required
                {...register('email')}
                error={errors.email?.message}
              />
              <Input label={t('common.phone')} {...register('phone')} />
              <Input label={t('leads.company')} {...register('company')} />
              <Input label={t('leads.jobTitle')} {...register('jobTitle')} />
              <div className="flex justify-end">
                <Button
                  type="submit"
                  disabled={!isDirty}
                  loading={updateLead.isPending}
                >
                  {t('common.save')}
                </Button>
              </div>
            </form>
          </div>

          {lead.nextBestActions && lead.nextBestActions.length > 0 && (
            <div className="rounded-lg border border-gray-200 bg-white p-6">
              <h2 className="mb-4 text-lg font-semibold">{t('ai.nextBestActions')}</h2>
              <ul className="space-y-2">
                {lead.nextBestActions.map((action, i) => (
                  <li key={i} className="flex items-start gap-2 text-sm">
                    <span className="mt-0.5 h-2 w-2 rounded-full bg-primary-500 flex-shrink-0" />
                    {action}
                  </li>
                ))}
              </ul>
            </div>
          )}
        </div>

        <div className="space-y-6">
          <div className="rounded-lg border border-gray-200 bg-white p-6">
            <h2 className="mb-4 text-lg font-semibold">{t('ai.draftMessage')}</h2>
            <AiDraftComposer
              entityType="lead"
              entityId={lead.id}
              onInsert={(draft) => { console.info('Draft ready:', draft.substring(0, 20)); }}
            />
          </div>
        </div>
      </div>
    </div>
  );
}
