from slowapi import Limiter
from slowapi.util import get_remote_address

# Backend em memória: vale para uma instância. Com várias, use storage_uri="redis://...".
limiter = Limiter(key_func=get_remote_address)
