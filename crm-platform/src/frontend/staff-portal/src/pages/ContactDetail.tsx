import { useParams, useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useForm } from 'react-hook-form';
import { z } from 'zod';
import { zodResolver } from '@hookform/resolvers/zod';
import { useContact, useUpdateContact } from '../hooks/useContacts';
import { Input, Button, Spinner } from '../components/ui';
import type { UpdateContactRequest } from '../types';

const schema = z.object({
  firstName: z.string().min(1),
  lastName: z.string().min(1),
  email: z.string().email(),
  phone: z.string().optional(),
  jobTitle: z.string().optional(),
});

type FormValues = z.infer<typeof schema>;

export function ContactDetail() {
  const { id } = useParams<{ id: string }>();
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { data: contact, isLoading } = useContact(id ?? '');
  const updateContact = useUpdateContact(id ?? '');

  const { register, handleSubmit, formState: { errors, isDirty } } = useForm<FormValues>({
    resolver: zodResolver(schema),
    values: contact
      ? {
          firstName: contact.firstName,
          lastName: contact.lastName,
          email: contact.email,
          phone: contact.phone ?? '',
          jobTitle: contact.jobTitle ?? '',
        }
      : undefined,
  });

  const onSubmit = (values: FormValues) => {
    const req: UpdateContactRequest = values;
    updateContact.mutate(req);
  };

  if (isLoading) {
    return <div className="flex h-64 items-center justify-center"><Spinner size="lg" /></div>;
  }
  if (!contact) {
    return <div className="p-6"><p className="text-gray-500">{t('errors.notFound')}</p></div>;
  }

  return (
    <div className="p-6 max-w-2xl mx-auto">
      <div className="mb-6 flex items-center gap-4">
        <button onClick={() => { void navigate('/contacts'); }} className="text-sm text-gray-500 hover:text-gray-700">
          ← {t('contacts.title')}
        </button>
        <h1 className="text-2xl font-bold text-gray-900">{contact.firstName} {contact.lastName}</h1>
      </div>

      <div className="rounded-lg border border-gray-200 bg-white p-6">
        <form onSubmit={(e) => { void handleSubmit(onSubmit)(e); }} className="space-y-4">
          <div className="grid grid-cols-2 gap-4">
            <Input label={t('contacts.firstName')} required {...register('firstName')} error={errors.firstName?.message} />
            <Input label={t('contacts.lastName')} required {...register('lastName')} error={errors.lastName?.message} />
          </div>
          <Input label={t('common.email')} type="email" required {...register('email')} error={errors.email?.message} />
          <Input label={t('common.phone')} {...register('phone')} />
          <Input label={t('contacts.jobTitle')} {...register('jobTitle')} />
          <div className="flex justify-end">
            <Button type="submit" disabled={!isDirty} loading={updateContact.isPending}>
              {t('common.save')}
            </Button>
          </div>
        </form>
      </div>
    </div>
  );
}
