const API_BASE = '/api';
const stripe = Stripe('pk_test_51S92nmLDEFw9YnAkt4DiLXEHMZv5TMjEc3JGYp7LvFhOOFx7rHFHXDyW3uNaHxZNpNeU6shjqKRlJCq3Iei6m4iz00oCL0glRB');

// ============== EXAMINER AUTHENTICATION VARIABLES ==============
let currentExaminer = null;
let examinerPortalContent = null;

// ============== INITIALIZATION ==============
document.addEventListener('DOMContentLoaded', function () {
    // Зберігаємо оригінальний контент Examiner Portal
    const examinerTab = document.getElementById('examinerTab');
    if (examinerTab) {
        examinerPortalContent = examinerTab.innerHTML;
    }

    // Перевіряємо автентифікацію
    checkExaminerAuth();

    // Обробник переключення на вкладку Examiner
    const examinerTabLink = document.querySelector('a[href="#examinerTab"]');
    if (examinerTabLink) {
        examinerTabLink.addEventListener('click', function (e) {
            if (!currentExaminer) {
                e.preventDefault();
                e.stopPropagation();
                hideExaminerPortal();
                // Активуємо вкладку програмно
                const tab = new bootstrap.Tab(examinerTabLink);
                tab.show();
            }
        });
    }

    // Initialize date input
    const tomorrow = new Date();
    tomorrow.setDate(tomorrow.getDate() + 1);
    tomorrow.setHours(10, 0, 0, 0);
    const dateStr = tomorrow.toISOString().slice(0, 16);

    const proposedDateTime = document.getElementById('proposedDateTime');
    if (proposedDateTime) {
        proposedDateTime.value = dateStr;
    }

    // Examiner login form handler
    const loginForm = document.getElementById('examinerLoginForm');
    if (loginForm) {
        loginForm.addEventListener('submit', handleExaminerLogin);
    }

    loadBookingFee();
});

async function loadBookingFee() {
    try {
        const response = await fetch('/api/Admin/settings/booking-fee');
        if (response.ok) {
            const data = await response.json();
            const fee = data.fee || 100;

            // Оновлюємо текст кнопки
            const submitText = document.getElementById('submitText');
            if (submitText) {
                submitText.textContent = `Proceed to Payment ($${fee})`;
            }

            // Оновлюємо суму в alert-і та на кнопці
            const feeDisplays = document.querySelectorAll('#bookingFeeDisplay, #buttonFeeDisplay');
            feeDisplays.forEach(el => {
                if (el) el.textContent = fee;
            });

            return fee;
        }
    } catch (error) {
        console.error('Error loading booking fee:', error);
        // При помилці встановлюємо default значення
        const submitText = document.getElementById('submitText');
        if (submitText) {
            submitText.textContent = 'Proceed to Payment ($100)';
        }

        const feeDisplays = document.querySelectorAll('#bookingFeeDisplay, #buttonFeeDisplay');
        feeDisplays.forEach(el => {
            if (el) el.textContent = '100';
        });
    }
    return 100;
}

// ============== EXAMINER AUTHENTICATION FUNCTIONS ==============
async function checkExaminerAuth() {
    try {
        const response = await fetch('/api/Examiner/check-auth');
        if (response.ok) {
            const result = await response.json();
            if (result.authenticated) {
                currentExaminer = result.examiner;
                console.log('Examiner authenticated:', currentExaminer);
                showExaminerPortal();
            } else {
                // Не викликаємо hideExaminerPortal тут, тільки коли юзер клікає на вкладку
            }
        }
    } catch (error) {
        console.error('Auth check error:', error);
    }
}

function hideExaminerPortal() {
    const examinerTab = document.getElementById('examinerTab');
    if (examinerTab) {
        examinerTab.innerHTML = `
            <div class="text-center py-5">
                <div class="card mx-auto" style="max-width: 400px;">
                    <div class="card-body">
                        <h3><i class="bi bi-lock"></i> Examiner Access Required</h3>
                        <p>Please login to access the Examiner Portal</p>
                        <button class="btn btn-info btn-lg" onclick="showExaminerLogin()">
                            <i class="bi bi-box-arrow-in-right"></i> Examiner Login
                        </button>
                    </div>
                </div>
            </div>
        `;
    }
}

function showExaminerPortal() {
    const examinerTab = document.getElementById('examinerTab');
    if (examinerTab && examinerPortalContent) {
        examinerTab.innerHTML = examinerPortalContent;

        // Автозаповнення полів
        if (currentExaminer) {
            setTimeout(() => {
                const emailField = document.getElementById('examinerEmail');
                const nameField = document.getElementById('examinerName');
                const filterField = document.getElementById('examinerEmailFilter');

                if (emailField) emailField.value = currentExaminer.email;
                if (nameField) nameField.value = currentExaminer.name;
                if (filterField) filterField.value = currentExaminer.email;

                // ДОДАЙТЕ ЦЕ - прикріплюємо обробник форми після відновлення HTML
                const responseForm = document.getElementById('examinerResponseForm');
                if (responseForm) {
                    responseForm.addEventListener('submit', handleExaminerResponse);
                }
            }, 100);
        }
    }
}

function showExaminerLogin() {
    const modal = new bootstrap.Modal(document.getElementById('examinerLoginModal'));
    modal.show();
}

async function handleExaminerLogin(e) {
    e.preventDefault();

    const username = document.getElementById('examinerLoginUsername').value;
    const password = document.getElementById('examinerLoginPassword').value;
    const errorDiv = document.getElementById('examinerLoginError');

    try {
        const response = await fetch('/api/Examiner/login', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                username: username,  // відправляємо як username
                password: password
            })
        });

        const result = await response.json();

        if (result.success) {
            currentExaminer = result.examiner;
            console.log('Login successful:', currentExaminer);

            const modal = bootstrap.Modal.getInstance(document.getElementById('examinerLoginModal'));
            if (modal) modal.hide();

            showExaminerPortal();
            alert(`Welcome, ${currentExaminer.name}!`);
        } else {
            errorDiv.textContent = result.message || 'Login failed';
            errorDiv.classList.remove('d-none');
        }
    } catch (error) {
        console.error('Login error:', error);
        errorDiv.textContent = 'Connection error';
        errorDiv.classList.remove('d-none');
    }
}

async function logoutExaminer() {
    try {
        await fetch('/api/Examiner/logout', { method: 'POST' });
        currentExaminer = null;
        location.reload();
    } catch (error) {
        console.error('Logout error:', error);
    }
}
async function handleExaminerResponse(e) {
    e.preventDefault();
    console.log('=== FORM SUBMITTED ===');

    // Перевірка автентифікації
    if (!currentExaminer) {
        alert('Please login as examiner first');
        showExaminerLogin();
        return;
    }

    const responseData = {
        bookingId: document.getElementById('bookingId').value,
        examinerEmail: document.getElementById('examinerEmail').value,
        examinerName: document.getElementById('examinerName').value,
        response: document.querySelector('input[name="response"]:checked')?.value,
        studentName: document.getElementById('studentName').value,
        studentEmail: document.getElementById('studentEmail').value,
        proposedDateTime: document.getElementById('proposedDateTime').value,
        responseMessage: document.getElementById('responseMessage').value,
        venueDetails: document.getElementById('venueDetails').value || '',
        examinerPrice: document.getElementById('examinerPrice').value || null,
        examinerPhone: document.getElementById('examinerPhone').value || ''
    };

    console.log('Response data:', responseData);

    try {
        const response = await fetch(`${API_BASE}/Booking/examiner/respond`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(responseData)
        });

        const result = await response.json();
        const resultDiv = document.getElementById('examinerResponseResult');

        if (response.ok) {
            if (result.assigned) {
                resultDiv.innerHTML = `
                    <div class="alert alert-success">
                        <h5>✅ Success!</h5>
                        <p>${result.message}</p>
                    </div>`;
            } else {
                resultDiv.innerHTML = `
                    <div class="alert alert-warning">
                        <h5>⚠️ Not Assigned</h5>
                        <p>${result.message}</p>
                    </div>`;
            }
        } else {
            resultDiv.innerHTML = `
                <div class="alert alert-danger">
                    <h5>❌ Error</h5>
                    <p>${result.message || 'An error occurred'}</p>
                </div>`;
        }
    } catch (error) {
        console.error('Caught error:', error);
        document.getElementById('examinerResponseResult').innerHTML = `
            <div class="alert alert-danger">
                <h5>❌ Network Error</h5>
                <p>Could not connect to server</p>
            </div>`;
    }
}
// ============== BOOKING FORM HANDLER ==============
document.getElementById('bookingForm').addEventListener('submit', async (e) => {
    e.preventDefault();
    e.stopPropagation();

    if (e.target.dataset.submitting === 'true') {
        return;
    }
    e.target.dataset.submitting = 'true';

    const submitBtn = e.target.querySelector('button[type="submit"]');
    const submitText = document.getElementById('submitText');
    const loadingSpinner = document.getElementById('loadingSpinner');

    submitBtn.disabled = true;
    submitText.textContent = 'Processing...';
    loadingSpinner.classList.remove('d-none');

    const asapChecked = document.getElementById('asapCheckbox').checked;
    const startDate = document.getElementById('startDate').value;
    const endDate = document.getElementById('endDate').value;

    const formData = {
        studentFirstName: document.getElementById('firstName').value,
        studentLastName: document.getElementById('lastName').value,
        studentEmail: document.getElementById('email').value,
        studentPhone: window.phoneIti ? window.phoneIti.getNumber() : document.getElementById('phone').value,
        aircraftType: document.getElementById('aircraftType').value,
        checkRideType: document.getElementById('checkRideType').value,
        preferredAirport: document.getElementById('preferredAirport').value,
        searchRadius: parseInt(document.getElementById('searchRadius').value) || 50,
        willingToFly: document.getElementById('willingToFly').checked,
        dateOption: asapChecked ? "ASAP" : "DATE_RANGE",
        startDate: asapChecked ? new Date().toISOString() : (startDate ? new Date(startDate).toISOString() : new Date().toISOString()),
        endDate: asapChecked ? new Date(Date.now() + 7 * 24 * 60 * 60 * 1000).toISOString() : (endDate ? new Date(endDate).toISOString() : new Date(Date.now() + 7 * 24 * 60 * 60 * 1000).toISOString()),
        ftnNumber: document.getElementById('ftnNumber').value || '',
        examId: document.getElementById('examId').value || '',
        additionalNotes: document.getElementById('additionalNotes').value || '',
        studentAddress: document.getElementById('preferredAirport').value,
        examType: document.getElementById('checkRideType').value,
        preferredDate: asapChecked ? new Date(Date.now() + 7 * 24 * 60 * 60 * 1000).toISOString() : (startDate ? new Date(startDate).toISOString() : new Date().toISOString()),
        preferredTime: '10:00',
        specialRequirements: document.getElementById('additionalNotes').value || ''
    };
    console.log('Phone being sent:', formData.studentPhone);

    try {
        const response = await fetch(`${API_BASE}/Payment/create-checkout-session`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(formData)
        });

        if (response.ok) {
            const result = await response.json();
            window.location.href = result.url;
        } else {
            const error = await response.text();
            showError(error);
        }
    } catch (error) {
        showError('Network error. Please check your connection.');
    } finally {
        submitBtn.disabled = false;
        loadBookingFee();
        loadingSpinner.classList.add('d-none');
        e.target.dataset.submitting = 'false';
    }
});

// ============== EXAMINER RESPONSE FORM ==============
document.getElementById('examinerResponseForm').addEventListener('submit', async (e) => {
    console.log('=== FORM SUBMITTED ===');
    e.preventDefault();

    // Перевірка автентифікації
    if (!currentExaminer) {
        alert('Please login as examiner first');
        showExaminerLogin();
        return;
    }

    const responseData = {
        bookingId: document.getElementById('bookingId').value,
        examinerEmail: document.getElementById('examinerEmail').value,
        examinerName: document.getElementById('examinerName').value,
        response: document.querySelector('input[name="response"]:checked').value,
        studentName: document.getElementById('studentName').value,
        studentEmail: document.getElementById('studentEmail').value,
        studentPhone: document.getElementById('studentPhone').value || '',
        proposedDateTime: document.getElementById('proposedDateTime').value,
        responseMessage: document.getElementById('responseMessage').value,
        venueDetails: document.getElementById('venueDetails').value || '',
        examinerPrice: document.getElementById('examinerPrice').value || null,
        examinerPhone: document.getElementById('examinerPhone').value || ''
    };

    try {
        const response = await fetch(`${API_BASE}/Booking/examiner/respond`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(responseData)
        });

        const result = await response.json();
        const resultDiv = document.getElementById('examinerResponseResult');

        if (response.ok) {
            if (result.assigned) {
                resultDiv.innerHTML = `
                    <div class="alert alert-success">
                        <h5>✅ Success!</h5>
                        <p>${result.message}</p>
                    </div>`;
            } else {
                resultDiv.innerHTML = `
                    <div class="alert alert-warning">
                        <h5>⚠️ Not Assigned</h5>
                        <p>${result.message}</p>
                    </div>`;
            }
        } else {
            resultDiv.innerHTML = `
                <div class="alert alert-danger">
                    <h5>❌ Error</h5>
                    <p>${result.message || 'An error occurred'}</p>
                </div>`;
        }
    } catch (error) {
        document.getElementById('examinerResponseResult').innerHTML = `
            <div class="alert alert-danger">
                <h5>❌ Network Error</h5>
                <p>Could not connect to server</p>
            </div>`;
    }
});

// ============== HELPER FUNCTIONS ==============
async function loadActiveBookings() {
    // Перевірка автентифікації
    if (!currentExaminer) {
        alert('Please login as examiner first');
        showExaminerLogin();
        return;
    }

    const listDiv = document.getElementById('activeBookingsList');
    listDiv.innerHTML = '<div class="spinner-border"></div> Loading...';

    try {
        const response = await fetch(`${API_BASE}/Booking/active`);

        if (response.ok) {
            const bookings = await response.json();

            if (bookings.length === 0) {
                listDiv.innerHTML = '<p class="text-muted">No active bookings at the moment</p>';
                return;
            }

            // ОНОВЛЕНО: додано wrapper для прокрутки
            let html = '<div class="table-scroll-wrapper">';
            html += '<table class="table table-striped">';
            html += '<thead><tr><th>Booking ID</th><th>Student</th><th>Email</th><th>Exam Type</th><th>Status</th><th>Paid</th><th>Created</th></tr></thead><tbody>';

            bookings.forEach(booking => {
                const statusBadge = getDetailedStatusBadge(booking.status, booking.assignedExaminerEmail, booking.assignedExaminerName);
                const paidBadge = booking.isPaid ? '<span class="badge bg-success">Paid</span>' : '<span class="badge bg-warning">Pending</span>';

                const bookingJson = JSON.stringify({
                    bookingId: booking.bookingId,
                    studentName: booking.studentName,
                    studentEmail: booking.studentEmail
                }).replace(/"/g, '&quot;');

                html += `
                    <tr style="cursor: pointer;" onclick='fillExaminerForm(${bookingJson})'>
                        <td><code>${booking.bookingId}</code></td>
                        <td>${booking.studentName}</td>
                        <td>${booking.studentEmail}</td>
                        <td>${booking.examType}</td>
                        <td>${statusBadge}</td>
                        <td>${paidBadge}</td>
                        <td>${new Date(booking.createdAt).toLocaleString()}</td>
                    </tr>`;
            });

            html += '</tbody></table></div>';
            // ДОДАНО: підказка для мобільних
            html += '<div class="scroll-hint"><i class="bi bi-arrow-left-right"></i> Scroll horizontally to see all columns</div>';
            listDiv.innerHTML = html;
        } else {
            listDiv.innerHTML = '<div class="alert alert-danger">Failed to load bookings</div>';
        }
    } catch (error) {
        listDiv.innerHTML = '<div class="alert alert-danger">Network error</div>';
    }
}


async function loadFilteredBookings() {
    // Перевірка автентифікації
    if (!currentExaminer) {
        alert('Please login as examiner first');
        showExaminerLogin();
        return;
    }

    const email = document.getElementById('examinerEmailFilter').value;
    if (!email) {
        alert('Please enter your email address');
        return;
    }

    const params = new URLSearchParams();
    params.append('examinerEmail', email);

    const listDiv = document.getElementById('availableBookingsList');
    listDiv.innerHTML = '<div class="spinner-border"></div> Loading...';

    try {
        const response = await fetch(`${API_BASE}/Booking/available-for-examiner?${params}`);
        if (response.ok) {
            const bookings = await response.json();

            if (bookings.length === 0) {
                listDiv.innerHTML = '<p class="text-muted">No available bookings match your criteria</p>';
                return;
            }

            // ОНОВЛЕНО: додано колонку Aircraft Type
            let html = '<div class="table-scroll-wrapper">';
            html += '<table class="table table-hover">';
            html += '<thead><tr><th>Booking ID</th><th>Student</th><th>Exam Type</th><th>Aircraft</th><th>Location</th><th>Preferred Date</th><th>Action</th></tr></thead><tbody>';

            bookings.forEach(booking => {
                html += `
                    <tr>
                        <td><code>${booking.bookingId}</code></td>
                        <td>${booking.studentName}</td>
                        <td><span class="badge bg-info">${booking.examType}</span></td>
                        <td><strong>${booking.aircraftType || 'N/A'}</strong></td>
                        <td>${booking.location}</td>
                        <td>${new Date(booking.preferredDate).toLocaleDateString()}</td>
                        <td>
                            <button class="btn btn-sm btn-success" 
                                onclick="fillResponseForm('${booking.bookingId}', '${booking.studentName.replace(/'/g, "\\'")}')">
                                Respond
                            </button>
                        </td>
                    </tr>`;
            });

            html += '</tbody></table></div>';
            html += '<div class="scroll-hint"><i class="bi bi-arrow-left-right"></i> Scroll horizontally to see all columns</div>';
            listDiv.innerHTML = html;
        }
    } catch (error) {
        listDiv.innerHTML = '<div class="alert alert-danger">Network error</div>';
    }
}

function getDetailedStatusBadge(status, assignedExaminerEmail, assignedExaminerName) {
    const hasAssignedExaminer = assignedExaminerEmail || assignedExaminerName;

    if (hasAssignedExaminer) {
        return '<span class="badge bg-danger">Taken</span>';
    }

    return '<span class="badge bg-success">Free</span>';
}

function fillExaminerForm(booking) {
    document.getElementById('bookingId').value = booking.bookingId;
    document.getElementById('studentName').value = booking.studentName;
    document.getElementById('studentEmail').value = booking.studentEmail;
    document.getElementById('studentPhone').value = booking.studentPhone || '';
}

function fillResponseForm(bookingId, studentName) {
    document.getElementById('bookingId').value = bookingId;
    document.getElementById('studentName').value = studentName;
    document.querySelector('.card-header.bg-gradient-info').scrollIntoView({ behavior: 'smooth' });
}

function showSuccess(result) {
    document.getElementById('studentForm').classList.add('d-none');
    document.getElementById('successMessage').classList.remove('d-none');
    document.getElementById('bookingIdDisplay').textContent = result.bookingId;
}

function showError(error) {
    document.getElementById('studentForm').classList.add('d-none');
    document.getElementById('errorMessage').classList.remove('d-none');
    document.getElementById('errorText').textContent = error || 'An unexpected error occurred';
}

function createNewBooking() {
    document.getElementById('bookingForm').reset();
    document.getElementById('studentForm').classList.remove('d-none');
    document.getElementById('successMessage').classList.add('d-none');
    document.getElementById('errorMessage').classList.add('d-none');
    document.getElementById('asapCheckbox').checked = true;
    toggleDateRange();
}

function retryBooking() {
    document.getElementById('studentForm').classList.remove('d-none');
    document.getElementById('errorMessage').classList.add('d-none');
}

function toggleDateRange() {
    const asapCheckbox = document.getElementById('asapCheckbox');
    const dateRangeSection = document.getElementById('dateRangeSection');

    if (asapCheckbox.checked) {
        dateRangeSection.style.display = 'none';
        document.getElementById('startDate').value = '';
        document.getElementById('endDate').value = '';
    } else {
        dateRangeSection.style.display = 'block';
        const tomorrow = new Date();
        tomorrow.setDate(tomorrow.getDate() + 1);
        const nextWeek = new Date();
        nextWeek.setDate(nextWeek.getDate() + 7);
        document.getElementById('startDate').value = tomorrow.toISOString().split('T')[0];
        document.getElementById('endDate').value = nextWeek.toISOString().split('T')[0];
    }
}

// ============== EXPORT FUNCTIONS ==============
window.showExaminerLogin = showExaminerLogin;
window.logoutExaminer = logoutExaminer;
window.loadActiveBookings = loadActiveBookings;
window.loadFilteredBookings = loadFilteredBookings;
window.fillResponseForm = fillResponseForm;
window.fillExaminerForm = fillExaminerForm;
window.createNewBooking = createNewBooking;
window.retryBooking = retryBooking;
window.toggleDateRange = toggleDateRange;