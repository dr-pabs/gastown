import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useCreateCase } from '../hooks/useCases';
import { Button, Input } from '../components/ui';
import type { CasePriority } from '../types';

const schema = z.object({
  subject: z.string().min(1, 'Subject is required').max(200),
  description: z.string().max(5000).optional(),
  priority: z.enum(['Low', 'Medium', 'High', 'Critical'] as const),
  category: z.string().max(100).optional(),
});

type FormValues = z.infer<typeof schema>;

const priorities: CasePriority[] = ['Low', 'Medium', 'High', 'Critical'];

export function NewCase() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const createCase = useCreateCase();

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { priority: 'Medium' },
  });

  const onSubmit = async (values: FormValues) => {
    const newCase = await createCase.mutateAsync(values);
    void navigate(`/cases/${newCase.id}`);
  };

  return (
    <div className="mx-auto max-w-2xl px-4 py-8">
      <div className="mb-6">
        <button
          type="button"
          onClick={() => { void navigate('/cases/my'); }}
          className="mb-2 text-sm text-blue-600 hover:underline"
        >
          ← {t('common.back')}
        </button>
        <h1 className="text-2xl font-bold text-gray-900">{t('cases.newCaseTitle')}</h1>
      </div>

      <form
        onSubmit={(e) => { void handleSubmit(onSubmit)(e); }}
        className="space-y-6 rounded-lg border border-gray-200 bg-white p-6 shadow-sm"
      >
        <div>
          <label className="mb-1 block text-sm font-medium text-gray-700">
            {t('cases.subject')} <span className="text-red-500">*</span>
          </label>
          <Input {...register('subject')} placeholder={t('cases.subjectPlaceholder')} />
          {errors.subject && (
            <p className="mt-1 text-xs text-red-600">{errors.subject.message}</p>
          )}
        </div>

        <div>
          <label className="mb-1 block text-sm font-medium text-gray-700">
            {t('cases.priority')}
          </label>
          <select
            {...register('priority')}
            className="block w-full rounded-md border border-gray-300 bg-white px-3 py-2 text-sm shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
          >
            {priorities.map((p) => (
              <option key={p} value={p}>{p}</option>
            ))}
          </select>
        </div>

        <div>
          <label className="mb-1 block text-sm font-medium text-gray-700">
            {t('cases.category')}
          </label>
          <Input {...register('category')} placeholder={t('cases.categoryPlaceholder')} />
        </div>

        <div>
          <label className="mb-1 block text-sm font-medium text-gray-700">
            {t('cases.description')}
          </label>
          <textarea
            {...register('description')}
            rows={6}
            placeholder={t('cases.descriptionPlaceholder')}
            className="block w-full rounded-md border border-gray-300 px-3 py-2 text-sm shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
          />
          {errors.description && (
            <p className="mt-1 text-xs text-red-600">{errors.description.message}</p>
          )}
        </div>

        <div className="flex gap-3 pt-2">
          <Button type="submit" loading={isSubmitting || createCase.isPending}>
            {t('cases.submitCase')}
          </Button>
          <Button
            type="button"
            variant="secondary"
            onClick={() => { void navigate('/cases/my'); }}
          >
            {t('common.cancel')}
          </Button>
        </div>

        {createCase.isError && (
          <p className="text-sm text-red-600">{t('errors.submitFailed')}</p>
        )}
      </form>
    </div>
  );
}
