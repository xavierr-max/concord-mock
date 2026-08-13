import { apiRequest } from './apiClient.js'

export const authApi = {
  login: payload => apiRequest('/api/auth/login', { method: 'POST', body: JSON.stringify(payload), anonymous: true }),
  register: payload => apiRequest('/api/auth/register', { method: 'POST', body: JSON.stringify(payload), anonymous: true }),
  me: () => apiRequest('/api/auth/me'),
  profile: () => apiRequest('/api/users/me'),
  updateProfile: payload => apiRequest('/api/users/me', { method: 'PUT', body: JSON.stringify(payload) }),
  updateAvatar: avatar => apiRequest('/api/users/me/avatar', { method: 'PUT', body: JSON.stringify({ avatar }) }),
  logout: refreshToken => apiRequest('/api/auth/logout', { method: 'POST', body: JSON.stringify({ refreshToken }) }),
}
