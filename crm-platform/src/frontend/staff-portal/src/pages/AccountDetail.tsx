import { useParams, useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useForm } from 'react-hook-form';
import { z } from 'zod';
import { zodResolver } from '@hookform/resolvers/zod';
import { useAccount, useUpdateAccount } from '../hooks/useAccounts';
import { Input, Button, Spinner } from '../components/ui';
import type { UpdateAccountRequest } from '../types';

const schema = z.object({
  name: z.string().min(1),
  industry: z.string().optional(),
  website: z.string().url().optional().or(z.literal('')),
  phone: z.string().optional(),
});

type FormValues = z.infer<typeof schema>;

export function AccountDetail() {
  const { id } = useParams<{ id: string }>();
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { data: account, isLoading } = useAccount(id ?? '');
  const updateAccount = useUpdateAccount(id ?? '');

  const { register, handleSubmit, formState: { errors, isDirty } } = useForm<FormValues>({
    resolver: zodResolver(schema),
    values: account
      ? {
          name: account.name,
          industry: account.industry ?? '',
          website: account.website ?? '',
          phone: account.phone ?? '',
        }
      : undefined,
  });

  const onSubmit = (values: FormValues) => {
    const req: UpdateAccountRequest = {
      name: values.name,
      industry: values.industry,
      website: values.website || undefined,
      phone: values.phone,
    };
    updateAccount.mutate(req);
  };

  if (isLoading) {
    return <div className="flex h-64 items-center justify-center"><Spinner size="lg" /></div>;
  }
  if (!account) {
    return <div className="p-6"><p className="text-gray-500">{t('errors.notFound')}</p></div>;
  }

  return (
    <div className="p-6 max-w-2xl mx-auto">
      <div className="mb-6 flex items-center gap-4">
        <button onClick={() => { void navigate('/accounts'); }} className="text-sm text-gray-500 hover:text-gray-700">
          ← {t('accounts.title')}
        </button>
        <h1 className="text-2xl font-bold text-gray-900">{account.name}</h1>
        {account.industry && (
          <span className="text-sm text-gray-500">{account.industry}</span>
        )}
      </div>

      <div className="rounded-lg border border-gray-200 bg-white p-6">
        <form onSubmit={(e) => { void handleSubmit(onSubmit)(e); }} className="space-y-4">
          <Input label={t('accounts.name')} required {...register('name')} error={errors.name?.message} />
          <Input label={t('accounts.industry')} {...register('industry')} />
          <Input label={t('accounts.website')} type="url" {...register('website')} error={errors.website?.message} />
          <Input label={t('common.phone')} {...register('phone')} />
          <div className="flex justify-end">
            <Button type="submit" disabled={!isDirty} loading={updateAccount.isPending}>
              {t('common.save')}
            </Button>
          </div>
        </form>
      </div>
    </div>
  );
}
