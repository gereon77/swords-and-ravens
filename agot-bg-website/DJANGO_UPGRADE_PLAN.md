# Django Upgrade Plan: 3.0.2 → 4.2 LTS → 5.1

## Executive Summary

This document outlines the comprehensive plan to upgrade from Django 3.0.2 to Django 4.2 LTS, and then to Django 5.1. The upgrade is necessary as Django 3.0 reached end-of-life in April 2021 and has known security vulnerabilities.

**Current State:**
- **Django:** 3.0.2 🔴 (EOL: April 2021 - 5 years ago)
- **Python (local):** 3.8.10 ✅
- **Python (server):** 3.6.9 🔴 (EOL: December 2021 - 4+ years ago)
- **Server OS:** Ubuntu 18.04.6 LTS (Bionic) 🔴 (EOL: April 2023 - 3 years ago)

**🚨 CRITICAL SECURITY RISK: Triple EOL Stack 🚨**

Your production server is running THREE end-of-life software versions with known security vulnerabilities:
- No security patches for Ubuntu since April 2023
- No security patches for Python since December 2021  
- No security patches for Django since April 2021

**BLOCKER:** Server Python 3.6.9 is NOT compatible with Django 4.x (requires 3.8+)

**Target State:**
- Django: 4.2 LTS (supported until April 2026) or 5.1
- Python: 3.10+ (required for Django 5.x)
- Server: Ubuntu 20.04 or 22.04

---

## 🔴 CONFIRMED: Critical Server Assessment

**Confirmed Server Configuration:**
- **OS:** Ubuntu 18.04.6 LTS (Bionic) - EOL since April 2023
- **Python 3:** 3.6.9 - EOL since December 2021
- **Django:** 3.0.2 - EOL since April 2021

**Python Compatibility Matrix:**
- Django 3.0.2: Python 3.6-3.8 ✅ (currently working)
- Django 4.x: Python 3.8+ ❌ (blocked by Python 3.6.9)
- Django 5.x: Python 3.10+ ❌ (blocked by Python 3.6.9)

**Bottom Line:** You CANNOT upgrade Django without first upgrading Python and likely the entire OS.

## Part 1: Django 3.0 → 4.2 LTS Migration

### 1.1 Prerequisites

#### Python Version Requirements
- **Django 4.0**: Python 3.8+ → Local: ✅ (3.8.10) | Server: ❌ (3.6.9)
- **Django 4.1**: Python 3.8+ → Local: ✅ (3.8.10) | Server: ❌ (3.6.9)
- **Django 4.2**: Python 3.8+ → Local: ✅ (3.8.10) | Server: ❌ (3.6.9)

**Local environment:** Python 3.8.10 ✅ Ready for Django 4.x

**Server environment:** Python 3.6.9 ❌ NOT compatible (EOL December 2021)

**BLOCKER:** You must upgrade server Python before Django can be upgraded.

#### Server Considerations - CRITICAL DECISIONS NEEDED

**Confirmed Server Issues:**
1. **Ubuntu 18.04.6 LTS** - EOL April 2023 (3 years without security updates) 🔴
2. **Python 2.7.17** as default - EOL January 2020 (6+ years vulnerable) 🔴
3. **Python 3.6.9** - EOL December 2021 (4+ years vulnerable, blocks Django 4.x) 🔴
4. **Django 3.0.2** - EOL April 2021 (5 years without security patches) 🔴

**You are running a completely unsupported stack. Immediate action strongly recommended.**

---

## Recommended Upgrade Paths

**Given your situation (triple-EOL stack), here are your realistic options:**

**Option A: Install Python 3.8 on Ubuntu 18.04 (Fastest, but risky)**
- Time: 2-4 hours
- Install Python 3.8 via deadsnakes PPA
- Create new virtual environment with Python 3.8
- Test and deploy Django 4.2 LTS
- **Pros:** Fast implementation, minimal changes
- **Cons:** Ubuntu 18.04 EOL (no security updates), technical debt
- **Lifespan:** 6-12 months maximum
- **Risk:** HIGH - running on unsupported OS
- **Recommended:** NO (only if desperate)

**Option B: Upgrade to Ubuntu 20.04 (Balanced - expiring soon)**
- Time: 1-2 days
- Upgrade Ubuntu 18.04 → 20.04 LTS
- Use built-in Python 3.8.10
- Upgrade to Django 4.2 LTS
- **Pros:** Native Python 3.8, official support
- **Cons:** Ubuntu 20.04 EOL in April 2025 (only 2 months away!)
- **Lifespan:** 2 months until OS EOL
- **Risk:** MEDIUM
- **Recommended:** NO (Ubuntu 20.04 EOL too soon)

**Option C: Upgrade to Ubuntu 22.04 (Recommended - future-proof) ⭐**
- Time: 1-2 days
- Upgrade Ubuntu 18.04 → 22.04 LTS (can skip 20.04)
- Use built-in Python 3.10.12
- Upgrade to Django 4.2 LTS initially (or go straight to 5.1)
- **Pros:** Python 3.10, Django 5.x capable, supported until 2027
- **Cons:** Major OS upgrade, more testing needed
- **Lifespan:** Until April 2027 (14+ months)
- **Risk:** MEDIUM-HIGH initially, LOW long-term
- **Recommended:** YES - best long-term investment

**Option D: New Ubuntu 22.04 Server (Most Reliable) ⭐⭐**
- Time: 2-4 days
- Provision fresh Ubuntu 22.04 LTS server
- Set up everything clean with Python 3.10
- Install Django 4.2 or 5.1
- Migrate database (PostgreSQL dump/restore)
- Test thoroughly in parallel
- Switch DNS when ready
- Keep old server as backup for 1-2 weeks
- **Pros:** Clean slate, no upgrade issues, easy rollback
- **Cons:** Most work upfront, temporary dual server cost
- **Lifespan:** Until April 2027+
- **Risk:** LOW - can test fully before switching
- **Recommended:** YES - safest and cleanest approach

### 1.2 Dependency Updates Required

Update `requirements.txt` with these changes:

```txt
# Core Framework
Django==4.2.11  # (from 3.0.2)
asgiref==3.7.2  # (from 3.2.3)

# Channels (WebSocket support)
channels==4.0.0  # (from 2.4.0) - Major version change
channels-redis==4.1.0  # (from 2.4.1)
daphne==4.0.0  # (from 2.4.1)

# Redis
aioredis==2.0.1  # (from 1.3.1) - Note: This is deprecated, channels-redis handles it

# Django Extensions
django-debug-toolbar==4.2.0  # (from 2.2)
djangorestframework==3.14.0  # (from 3.11.0)
django-storages==1.14.2  # (from 1.12.3)
social-auth-app-django==5.4.0  # (from 3.1.0)
social-auth-core==4.5.1  # (from 3.2.0)
django-environ==0.11.2  # (from 0.4.5)
django-bootstrap4==23.2  # (from 1.1.1)

# Database
psycopg2-binary==2.9.9  # (from 2.8.3)

# Security & Dependencies
cryptography==41.0.7  # (from 2.8) - CRITICAL security updates
pyOpenSSL==23.3.0  # (from 19.1.0)
urllib3==2.1.0  # (from 1.25.4)
requests==2.31.0  # (from 2.22.0)
certifi==2023.11.17  # (from 2019.6.16)

# AWS
boto3==1.34.16  # (from 1.22.4)
botocore==1.34.16  # (from 1.25.4)

# Utilities
whitenoise==6.6.0  # (from 5.0.1)
sentry-sdk==1.39.2  # (from 0.14.0)
beautifulsoup4==4.12.2  # (from 4.8.2)

# Twisted (for Channels)
Twisted==23.10.0  # (from 19.10.0)
```

### 1.3 Code Changes Required

#### 1.3.1 URL Patterns (HIGH PRIORITY)

**File:** `agotboardgame/urls.py`

```python
# BEFORE
from django.conf.urls import url
from django.urls import path, include

urlpatterns = [
    path('', include('agotboardgame_main.urls')),
    url('', include('social_django.urls', namespace='social')),  # ❌ OLD
    # ...
]

# AFTER
from django.urls import path, include, re_path

urlpatterns = [
    path('', include('agotboardgame_main.urls')),
    path('', include('social_django.urls', namespace='social')),  # ✅ NEW
    # If you need regex patterns, use re_path:
    # re_path(r'^some-pattern/', view),
]
```

**File:** `agotboardgame_main/urls.py`

```python
# BEFORE
from django.conf.urls import url
from django.urls import path, include

urlpatterns = [
    # ... paths ...
    url('', include('django_prometheus.urls'))  # ❌ OLD
]

# AFTER
from django.urls import path, include

urlpatterns = [
    # ... paths ...
    path('', include('django_prometheus.urls'))  # ✅ NEW
]
```

#### 1.3.2 JSONField Import (HIGH PRIORITY)

**File:** `agotboardgame_main/models.py`

```python
# BEFORE
from django.contrib.postgres.fields import JSONField  # ❌ Deprecated

class Game(models.Model):
    view_of_game = JSONField(null=True, default=generate_default_view_of_game, blank=True)
    serialized_game = JSONField(null=True, default=None, blank=True)

# AFTER
from django.db.models import JSONField  # ✅ New location

class Game(models.Model):
    view_of_game = JSONField(null=True, default=generate_default_view_of_game, blank=True)
    serialized_game = JSONField(null=True, default=None, blank=True)
```

#### 1.3.3 Settings.py Updates (HIGH PRIORITY)

**File:** `agotboardgame/settings.py`

Add these settings:

```python
# After DATABASES configuration, add:

# Default primary key field type
# https://docs.djangoproject.com/en/4.2/ref/settings/#default-auto-field
DEFAULT_AUTO_FIELD = 'django.db.models.BigAutoField'

# CSRF settings (Django 4.0+)
CSRF_TRUSTED_ORIGINS = [
    'https://swordsandravens.net',
    'https://*.swordsandravens.net',
    # Add your domains here
]

# Update USE_L10N (deprecated in Django 4.0, removed in 5.0)
# Remove this line or keep it for Django 4.x:
USE_L10N = True  # This will be ignored in Django 4.0+ but won't cause errors
```

#### 1.3.4 ASGI Configuration (MEDIUM PRIORITY)

**File:** `agotboardgame/asgi.py`

```python
# BEFORE
import os
import django
from channels.routing import get_default_application

os.environ.setdefault("DJANGO_SETTINGS_MODULE", "agotboardgame.settings")
django.setup()
application = get_default_application()

# AFTER
import os
from django.core.asgi import get_asgi_application
from channels.routing import ProtocolTypeRouter, URLRouter
from channels.auth import AuthMiddlewareStack
import chat.routing

os.environ.setdefault("DJANGO_SETTINGS_MODULE", "agotboardgame.settings")

django_asgi_app = get_asgi_application()

# Import routing after Django setup
application = ProtocolTypeRouter({
    "http": django_asgi_app,
    "websocket": AuthMiddlewareStack(
        URLRouter(
            chat.routing.websocket_urlpatterns
        )
    ),
})
```

**File:** `agotboardgame/routing.py`

```python
# This file can be deleted if you update asgi.py as shown above
# Or update it to:
from channels.routing import ProtocolTypeRouter, URLRouter
from channels.auth import AuthMiddlewareStack
from django.core.asgi import get_asgi_application
import chat.routing

application = ProtocolTypeRouter({
    "http": get_asgi_application(),
    "websocket": AuthMiddlewareStack(
        URLRouter(
            chat.routing.websocket_urlpatterns
        )
    ),
})
```

#### 1.3.5 Model Field Default Values (MEDIUM PRIORITY)

**File:** `agotboardgame_main/models.py`

```python
# BEFORE
last_active_at = models.DateTimeField(default=django.utils.timezone.now())  # ❌ Called at class definition

# AFTER
last_active_at = models.DateTimeField(default=django.utils.timezone.now)  # ✅ Pass function reference
```

Remove the parentheses from `timezone.now()` to pass the function itself, not the result.

### 1.4 Testing Strategy

#### Local Testing Checklist

1. **Create a test branch:**
   ```bash
   git checkout -b upgrade/django-4.2
   ```

2. **Update dependencies:**
   ```bash
   cd agot-bg-website
   pip install --upgrade pip
   pip install -r requirements.txt
   ```

3. **Run migrations:**
   ```bash
   python manage.py makemigrations
   python manage.py migrate
   ```

4. **Check for deprecation warnings:**
   ```bash
   python -W default manage.py check
   ```

5. **Run existing tests:**
   ```bash
   python manage.py test
   ```

6. **Manual testing:**
   - User authentication (login, logout, social auth)
   - Game creation and joining
   - WebSocket connections (chat)
   - Admin panel
   - API endpoints
   - Static file serving
   - Database queries performance

7. **Check dependencies compatibility:**
   ```bash
   pip check
   ```

### 1.5 Deployment Strategy

#### Option A: Gradual Rollout (Recommended)

1. **Set up staging environment:**
   - Clone production database to staging
   - Deploy Django 4.2 code to staging
   - Test thoroughly for 1-2 weeks

2. **Prepare production:**
   - Schedule maintenance window
   - Backup database
   - Deploy new code
   - Run migrations
   - Monitor logs and errors

#### Option B: Blue-Green Deployment

1. Set up parallel environment with Django 4.2
2. Test with copy of production data
3. Switch traffic when ready
4. Keep old environment for quick rollback

---

## Part 2: Django 4.2 LTS → 5.1 Migration

### 2.1 Prerequisites

#### Python Version Requirements
- **Django 5.0**: Python 3.10+  ❌ (you have 3.8.10)
- **Django 5.1**: Python 3.10+  ❌

**BLOCKER:** You must upgrade Python before moving to Django 5.x

#### Server Upgrade Required

Ubuntu 18.04 with Python 3.8 → Ubuntu 20.04/22.04 with Python 3.10+

**Options:**
1. **Ubuntu 20.04:** Python 3.10 available via deadsnakes PPA
2. **Ubuntu 22.04:** Python 3.10 is default ✅ (Recommended)

### 2.2 Python Upgrade Strategy

#### Option 1: Upgrade Server OS (Recommended)

```bash
# Backup everything first!
# Then upgrade Ubuntu 18.04 → 20.04 → 22.04

# On Ubuntu 22.04, Python 3.10 is default
python3 --version  # Should show 3.10.x
```

#### Option 2: Install Python 3.10 on Ubuntu 18.04/20.04

```bash
# Add deadsnakes PPA
sudo add-apt-repository ppa:deadsnakes/ppa
sudo apt update
sudo apt install python3.10 python3.10-venv python3.10-dev

# Create new virtual environment with Python 3.10
python3.10 -m venv venv
source venv/bin/activate
```

### 2.3 Major Breaking Changes (Django 4.2 → 5.x)

#### 2.3.1 Removed USE_L10N Setting

Django 5.0 removes the `USE_L10N` setting (it's always enabled).

**Action:** Remove this line from `settings.py`:
```python
USE_L10N = True  # ❌ Remove in Django 5.0
```

#### 2.3.2 Database Backend Changes

- PostgreSQL 12+ required (Django 5.0+)
- Check your PostgreSQL version:
  ```bash
  psql --version
  ```

#### 2.3.3 Removed django.utils.encoding Functions

Functions like `force_text()` were removed. If you use them, replace with:
- `force_text()` → `force_str()`
- `smart_text()` → `smart_str()`

### 2.4 Dependency Updates (Django 5.1)

```txt
Django==5.1.3
asgiref==3.7.2
channels==4.0.0  # Same as Django 4.2
djangorestframework==3.15.0
psycopg2-binary==2.9.9  # Or consider psycopg 3.x
# ... other dependencies remain similar
```

### 2.5 Testing for Django 5.x

Same strategy as Part 1, but with additional focus on:
- Database compatibility (PostgreSQL 12+)
- Python 3.10+ specific features
- Dependencies that may not yet support Django 5.x

---

## Part 3: Risk Assessment

### 3.1 High-Risk Areas

1. **Channels/WebSocket:** Major version change (2.x → 4.x)
   - API changes in consumers
   - Different middleware stack
   - Connection handling changes

2. **Social Auth:** Version jump (3.1 → 5.4)
   - OAuth flow might need testing
   - Pipeline changes

3. **Database Migrations:** Large version jump
   - Test migrations thoroughly
   - Have rollback plan

### 3.2 Medium-Risk Areas

1. **Django Debug Toolbar:** Different UI/behavior
2. **Django REST Framework:** API serialization changes
3. **Static files handling:** Whitenoise updates

### 3.3 Low-Risk Areas

1. **Models:** Mostly compatible
2. **Templates:** No major changes
3. **Admin:** Backward compatible

---

## Part 4: Recommended Approach

### Phased Migration (Recommended)

#### Phase 1: Prepare (Week 1-2)
1. Set up local development with Django 4.2
2. Make all code changes
3. Update dependencies
4. Run comprehensive tests locally

#### Phase 2: Staging (Week 3-4)
1. Deploy to staging environment
2. Run with production data copy
3. Monitor for issues
4. Performance testing

#### Phase 3: Production (Week 5)
1. Schedule maintenance window (2-4 hours)
2. Backup everything
3. Deploy Django 4.2
4. Monitor closely for 1 week
5. Keep rollback plan ready

#### Phase 4: Django 5.x (3-6 months later)
1. Upgrade server to Ubuntu 22.04 with Python 3.10+
2. Test Django 5.1 in development
3. Repeat staging → production process

### Alternative: Skip to Django 4.2 LTS

**Recommendation:** Stay on Django 4.2 LTS
- Supported until April 2026
- Stable and well-tested
- No server upgrade required
- All security fixes

Delay Django 5.x upgrade until:
- You upgrade server OS naturally
- All dependencies mature on Django 5.x
- More urgent priorities completed

---

## Part 5: Detailed Code Changes Checklist

### Files to Modify

- [ ] `requirements.txt` - Update all dependencies
- [ ] `agotboardgame/settings.py` - Add DEFAULT_AUTO_FIELD, CSRF_TRUSTED_ORIGINS
- [ ] `agotboardgame/urls.py` - Replace url() with path()
- [ ] `agotboardgame/asgi.py` - Update ASGI application
- [ ] `agotboardgame/routing.py` - Update or remove
- [ ] `agotboardgame_main/urls.py` - Replace url() with path()
- [ ] `agotboardgame_main/models.py` - Update JSONField import, fix timezone.now()

### Commands to Run

```bash
# 1. Create virtual environment (if needed)
python3.8 -m venv venv
source venv/bin/activate

# 2. Upgrade pip
pip install --upgrade pip

# 3. Install updated dependencies
pip install -r requirements.txt

# 4. Check for issues
python manage.py check

# 5. Generate migrations (if any)
python manage.py makemigrations

# 6. Show migration plan
python manage.py showmigrations

# 7. Run migrations
python manage.py migrate

# 8. Check for deployment issues
python manage.py check --deploy

# 9. Collect static files
python manage.py collectstatic --noinput

# 10. Run tests
python manage.py test
```

---

## Part 6: Rollback Plan

### If Issues Occur

1. **Stop services:**
   ```bash
   sudo systemctl stop gunicorn
   sudo systemctl stop daphne
   ```

2. **Restore code:**
   ```bash
   git checkout main  # or previous stable branch
   ```

3. **Restore database:** (if migrations ran)
   ```bash
   # Restore from backup taken before upgrade
   psql dbname < backup.sql
   ```

4. **Reinstall old dependencies:**
   ```bash
   pip install -r requirements.txt.backup
   ```

5. **Restart services:**
   ```bash
   sudo systemctl start gunicorn
   sudo systemctl start daphne
   ```

---

## Part 7: Monitoring After Upgrade

### Critical Metrics to Watch

1. **Error rates:** Check Sentry/logs for new exceptions
2. **Response times:** Monitor performance regression
3. **WebSocket connections:** Ensure chat still works
4. **User authentication:** Social auth functionality
5. **Database queries:** Check for N+1 queries or slow queries
6. **Memory usage:** Monitor for memory leaks
7. **Task queue:** If using Celery or similar

### Log Files to Monitor

```bash
# Application logs
tail -f /var/log/gunicorn/error.log
tail -f /var/log/daphne/error.log

# System logs
journalctl -u gunicorn -f
journalctl -u daphne -f
Recommendations

### 🚨 Critical Reality Check

**You cannot do a "quick win" Django-only upgrade.** Your Python 3.6.9 blocks Django 4.x entirely.

### Recommended Path: New Ubuntu 22.04 Server (Option D)

**Why this is best:**
1. Clean, tested setup without upgrade complications
2. Can test thoroughly before switching traffic
3. Easy rollback if issues occur
4. Eliminates all four EOL components at once
5. Future-proof until 2027+

**Timeline:**
- **Week 1:** Provision new Ubuntu 22.04 server, set up environment
- **Week 2:** Deploy code, migrate database, configure services
- **Week 3:** Testing and validation in parallel with production
- **Week 4:** Switch traffic, monitor, keep old server as backup

**Total Time:** 3-4 weeks to fully migrate

**Cost:** ~$5-10/month for 2-4 weeks of dual servers

### Alternative Path: In-Place Upgrade (Option C)

Upgrade Ubuntu 18.04 → 22.04 in place (can skip 20.04):

**Pros:**
- Uses existing server
- No data migration needed

**Cons:**
- Riskier (one server, no parallel testing)
- Downtime during upgrade (2-4 hours)
- Harder to rollback if issues occur

**Timeline:** 1-2 weeks with higher risk

### NOT Recommended: Band-Aid Python 3.8 Install (Option A)

Installing Python 3.8 on EOL Ubuntu 18.04 is **not advisable** because:
- Still running unsupported OS
- Only delays the inevitable
- Technical debt accumulation
- Security vulnerabilities remain in OS layer
**Blocker:** Requires Python 3.10+ and server upgrade

### Recommendation

1. **Immediate:** Upgrade to Django 4.2 LTS
2. **6-12 months:** Plan server OS upgrade to Ubuntu 22.04
3. **12-18 months:** Evaluate Django 5.x upgrade

---

## Questions or Issues?

Document any issues encountered during the upgrade:
- Unexpected errors
- Performance regressions
- Compatibility issues
- Rollback procedures used

Good luck with the upgrade! 🚀
