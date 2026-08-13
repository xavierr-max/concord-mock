import { useContext } from 'react'
import { AuthContext } from '../contexts/authContext.js'

export const useAuth = () => useContext(AuthContext)
