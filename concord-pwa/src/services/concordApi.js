import { apiRequest } from './apiClient.js'

export const concordApi = {
  servers: {
    list: () => apiRequest('/api/servers'),
    get: id => apiRequest(`/api/servers/${id}`),
    create: payload => apiRequest('/api/servers', { method: 'POST', body: JSON.stringify(payload) }),
    update: (id, payload) => apiRequest(`/api/servers/${id}`, { method: 'PUT', body: JSON.stringify(payload) }),
    remove: id => apiRequest(`/api/servers/${id}`, { method: 'DELETE' }),
    leaveOrRemoveMember: (id, userId) => apiRequest(`/api/servers/${id}/members/${userId}`, { method: 'DELETE' }),
  },
  channels: {
    list: serverId => apiRequest(`/api/servers/${serverId}/channels`),
    create: (serverId, payload) => apiRequest(`/api/servers/${serverId}/channels`, { method: 'POST', body: JSON.stringify(payload) }),
    update: (id, payload) => apiRequest(`/api/channels/${id}`, { method: 'PUT', body: JSON.stringify(payload) }),
    remove: id => apiRequest(`/api/channels/${id}`, { method: 'DELETE' }),
    unread: id => apiRequest(`/api/channels/${id}/unread-count`),
    markRead: id => apiRequest(`/api/channels/${id}/read`, { method: 'POST' }),
  },
  messages: {
    list: (channelId, page = 1, pageSize = 50) => apiRequest(`/api/channels/${channelId}/messages?page=${page}&pageSize=${pageSize}`),
    send: (channelId, content) => apiRequest(`/api/channels/${channelId}/messages`, { method: 'POST', body: JSON.stringify({ content }) }),
    update: (id, content) => apiRequest(`/api/messages/${id}`, { method: 'PUT', body: JSON.stringify({ content }) }),
    remove: id => apiRequest(`/api/messages/${id}`, { method: 'DELETE' }),
    attach: (id, file) => { const body = new FormData(); body.append('file', file); return apiRequest(`/api/messages/${id}/attachments`, { method: 'POST', body }) },
  },
  invites: {
    create: (serverId, payload) => apiRequest(`/api/servers/${serverId}/invites`, { method: 'POST', body: JSON.stringify(payload) }),
    get: code => apiRequest(`/api/invites/${encodeURIComponent(code)}`),
    accept: code => apiRequest(`/api/invites/${encodeURIComponent(code)}/accept`, { method: 'POST' }),
    remove: code => apiRequest(`/api/invites/${encodeURIComponent(code)}`, { method: 'DELETE' }),
  },
  notifications: {
    list: (page = 1, pageSize = 30) => apiRequest(`/api/notifications?page=${page}&pageSize=${pageSize}`),
    unread: () => apiRequest('/api/notifications/unread-count'),
    markRead: id => apiRequest(`/api/notifications/${id}/read`, { method: 'POST' }),
    markAllRead: () => apiRequest('/api/notifications/read-all', { method: 'POST' }),
  },
}
