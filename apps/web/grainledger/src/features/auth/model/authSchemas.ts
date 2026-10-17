import { z } from 'zod';

export const signInSchema = z.object({
  email: z
    .string()
    .min(1, 'Email không được để trống.')
    .email('Định dạng email không hợp lệ.'),
  password: z
    .string()
    .min(8, 'Mật khẩu phải có ít nhất 8 ký tự.'),
  rememberMe: z.boolean(),
});

export type SignInFormValues = z.infer<typeof signInSchema>;

export const acceptInvitationSchema = z
  .object({
    password: z
      .string()
      .min(12, 'Mật khẩu phải có tối thiểu 12 ký tự.')
      .regex(/[A-Z]/, 'Mật khẩu phải chứa ít nhất 1 chữ in hoa.')
      .regex(/[a-z]/, 'Mật khẩu phải chứa ít nhất 1 chữ thường.')
      .regex(/[0-9]/, 'Mật khẩu phải chứa ít nhất 1 chữ số.')
      .regex(/[^A-Za-z0-9]/, 'Mật khẩu phải chứa ít nhất 1 ký tự đặc biệt.'),
    confirmPassword: z.string().min(1, 'Vui lòng xác nhận mật khẩu.'),
  })
  .refine((data) => data.password === data.confirmPassword, {
    message: 'Mật khẩu xác nhận không khớp.',
    path: ['confirmPassword'],
  });

export type AcceptInvitationFormValues = z.infer<typeof acceptInvitationSchema>;

export const changePasswordSchema = z
  .object({
    currentPassword: z.string().min(1, 'Vui lòng nhập mật khẩu hiện tại.'),
    newPassword: z
      .string()
      .min(12, 'Mật khẩu mới phải có tối thiểu 12 ký tự.')
      .regex(/[A-Z]/, 'Mật khẩu phải chứa ít nhất 1 chữ in hoa.')
      .regex(/[a-z]/, 'Mật khẩu phải chứa ít nhất 1 chữ thường.')
      .regex(/[0-9]/, 'Mật khẩu phải chứa ít nhất 1 chữ số.')
      .regex(/[^A-Za-z0-9]/, 'Mật khẩu phải chứa ít nhất 1 ký tự đặc biệt.'),
    confirmNewPassword: z.string().min(1, 'Vui lòng xác nhận lại mật khẩu mới.'),
  })
  .refine((data) => data.newPassword === data.confirmNewPassword, {
    message: 'Mật khẩu xác nhận không khớp.',
    path: ['confirmNewPassword'],
  })
  .refine((data) => data.currentPassword !== data.newPassword, {
    message: 'Mật khẩu mới phải khác mật khẩu hiện tại.',
    path: ['newPassword'],
  });

export type ChangePasswordFormValues = z.infer<typeof changePasswordSchema>;

export const editProfileSchema = z.object({
  displayName: z
    .string()
    .min(2, 'Họ và tên phải có ít nhất 2 ký tự.')
    .max(120, 'Họ và tên tối đa 120 ký tự.'),
  language: z.enum(['vi', 'en']),
  phoneNumber: z
    .string()
    .optional()
    .refine(
      (val) => !val || /^[0-9+() -]{9,15}$/.test(val),
      'Số điện thoại không đúng định dạng.'
    ),
});

export type EditProfileFormValues = z.infer<typeof editProfileSchema>;
