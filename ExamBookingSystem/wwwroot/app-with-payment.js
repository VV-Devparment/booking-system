const API_BASE = '/api';
const stripe = Stripe('pk_test_51Ri7PbECQKRSCDpzi5n5B0oclWVCPAbT32F1v3zEIooF0avPQTX2XWsjsTkF2sTPQgWnGIl8Ovd08JEUNxWUcEie00u5qO5lpt');

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

    // Collect form data with new fields
    const asapChecked = document.getElementById('asapCheckbox').checked;
    const startDate = document.getElementById('startDate').value;
    const endDate = document.getElementById('endDate').value;

    const formData = {
        studentFirstName: document.getElementById('firstName').value,
        studentLastName: document.getElementById('lastName').value,
        studentEmail: document.getElementById('email').value,
        studentPhone: window.phoneIti ? window.phoneIti.getNumber() : document.getElementById('phone').value,

        // Aircraft and exam info
        aircraftType: document.getElementById('aircraftType').value,
        checkRideType: document.getElementById('checkRideType').value,
        preferredAirport: document.getElementById('preferredAirport').value,
        searchRadius: parseInt(document.getElementById('searchRadius').value) || 50,
        willingToFly: document.getElementById('willingToFly').checked,

        // New availability window fields
        dateOption: asapChecked ? "ASAP" : "DATE_RANGE",
        startDate: asapChecked ? new Date().toISOString() : (startDate ? new Date(startDate).toISOString() : new Date().toISOString()),
        endDate: asapChecked ? new Date(Date.now() + 7 * 24 * 60 * 60 * 1000).toISOString() : (endDate ? new Date(endDate).toISOString() : new Date(Date.now() + 7 * 24 * 60 * 60 * 1000).toISOString()),

        // New additional fields
        ftnNumber: document.getElementById('ftnNumber').value || '',
        examId: document.getElementById('examId').value || '',
        additionalNotes: document.getElementById('additionalNotes').value || '',

        // Legacy compatibility fields
        studentAddress: document.getElementById('preferredAirport').value,
        examType: document.getElementById('checkRideType').value,
        preferredDate: asapChecked ? new Date(Date.now() + 7 * 24 * 60 * 60 * 1000).toISOString() : (startDate ? new Date(startDate).toISOString() : new Date().toISOString()),
        preferredTime: '10:00',
        specialRequirements: document.getElementById('additionalNotes').value || ''
    };

    // Перевірка наявності всіх полів
    const requiredFields = {
        firstName: document.getElementById('firstName')?.value,
        lastName: document.getElementById('lastName')?.value,
        email: document.getElementById('email')?.value,
        aircraftType: document.getElementById('aircraftType')?.value,
        checkRideType: document.getElementById('checkRideType')?.value,
        preferredAirport: document.getElementById('preferredAirport')?.value
    };

    console.log('Required fields check:', requiredFields);

    // Якщо якесь поле відсутнє
    for (const [field, value] of Object.entries(requiredFields)) {
        if (!value) {
            console.error(`Missing required field: ${field}`);
            alert(`Please fill in: ${field}`);
            submitBtn.disabled = false;
            submitText.textContent = 'Proceed to Payment ($100)';
            loadingSpinner.classList.add('d-none');
            e.target.dataset.submitting = 'false';
            return;
        }
    }

    console.log('Form data being sent:', formData);

    try {
        const response = await fetch(`${API_BASE}/Payment/create-checkout-session`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(formData)
        });

        if (response.ok) {
            const result = await response.json();
            console.log('Checkout session created:', result);
            window.location.href = result.url;
        } else {
            const error = await response.text();
            console.error('Checkout session error:', error);
            showError(error);
        }
    } catch (error) {
        console.error('Network error:', error);
        showError('Network error. Please check your connection.');
    } finally {
        submitBtn.disabled = false;
        submitText.textContent = 'Proceed to Payment ($100)';
        loadingSpinner.classList.add('d-none');
        e.target.dataset.submitting = 'false';
    }
});

document.getElementById('examinerResponseForm').addEventListener('submit', async (e) => {
    e.preventDefault();

    const bookingId = document.getElementById('bookingId').value;

    // ДОДАНА ПЕРЕВІРКА: Блокуємо форму якщо букінг вже взятий
    if (bookingId) {
        try {
            const checkResponse = await fetch(`${API_BASE}/Booking/${bookingId}`);
            if (checkResponse.ok) {
                const booking = await checkResponse.json();
                if (booking.status === 'ExaminerAssigned' ||
                    booking.status === 'Scheduled' ||
                    booking.assignedExaminerEmail) {
                    alert('This booking has already been assigned to another examiner.');
                    // Заблокувати форму та кнопку
                    const submitBtn = document.querySelector('#examinerResponseForm button[type="submit"]');
                    submitBtn.disabled = true;
                    submitBtn.innerHTML = '<i class="bi bi-lock"></i> Booking Taken';
                    submitBtn.classList.add('btn-secondary');
                    submitBtn.classList.remove('btn-info');
                    return;
                }
            }
        } catch (error) {
            console.error('Error checking booking status:', error);
        }
    }

    const responseData = {
        bookingId: document.getElementById('bookingId').value,
        examinerEmail: document.getElementById('examinerEmail').value,
        examinerName: document.getElementById('examinerName').value,
        response: document.querySelector('input[name="response"]:checked').value,
        studentName: document.getElementById('studentName').value,
        studentEmail: document.getElementById('studentEmail').value,
        proposedDateTime: document.getElementById('proposedDateTime').value,
        responseMessage: document.getElementById('responseMessage').value,
        venueDetails: document.getElementById('venueDetails').value || '',
        examinerPrice: document.getElementById('examinerPrice').value || null,
        examinerPhone: document.getElementById('examinerPhone').value || ''
    };

    try {
        const response = await fetch(`${API_BASE}/Booking/examiner/respond`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
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
                    </div>
                `;
                // Заблокувати форму після успішної відповіді
                const submitBtn = document.querySelector('#examinerResponseForm button[type="submit"]');
                submitBtn.disabled = true;
                submitBtn.innerHTML = '<i class="bi bi-check-circle"></i> Response Sent';
                submitBtn.classList.add('btn-success');
                submitBtn.classList.remove('btn-info');
            } else {
                resultDiv.innerHTML = `
                    <div class="alert alert-warning">
                        <h5>⚠️ Not Assigned</h5>
                        <p>${result.message}</p>
                    </div>
                `;
            }
        } else {
            resultDiv.innerHTML = `
                <div class="alert alert-danger">
                    <h5>❌ Error</h5>
                    <p>${result.message || 'An error occurred'}</p>
                </div>
            `;
        }
    } catch (error) {
        document.getElementById('examinerResponseResult').innerHTML = `
            <div class="alert alert-danger">
                <h5>❌ Network Error</h5>
                <p>Could not connect to server</p>
            </div>
        `;
    }
});

function showSuccess(result) {
    document.getElementById('studentForm').classList.add('d-none');
    document.getElementById('successMessage').classList.remove('d-none');
    document.getElementById('bookingIdDisplay').textContent = result.bookingId;

    const examinersList = document.getElementById('examinersList');
    examinersList.innerHTML = '';

    if (result.examinersContacted && result.examinersContacted.length > 0) {
        result.examinersContacted.forEach(examiner => {
            const li = document.createElement('li');
            li.textContent = examiner;
            examinersList.appendChild(li);
        });
    }
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

    // Reset ASAP checkbox to checked
    document.getElementById('asapCheckbox').checked = true;
    toggleDateRange();
}

function retryBooking() {
    document.getElementById('studentForm').classList.remove('d-none');
    document.getElementById('errorMessage').classList.add('d-none');
}

function showExaminerPanel() {
    const modal = new bootstrap.Modal(document.getElementById('examinerModal'));
    modal.show();
}

function showActiveBookings() {
    const modal = new bootstrap.Modal(document.getElementById('activeBookingsModal'));
    modal.show();
    loadActiveBookings();
}

// ОНОВЛЕНА ФУНКЦІЯ loadActiveBookings - передаємо дані про екзаменатора
async function loadActiveBookings() {
    const listDiv = document.getElementById('activeBookingsList');
    listDiv.innerHTML = '<div class="spinner-border"></div> Loading...';

    try {
        console.log('Loading active bookings...');
        const response = await fetch(`${API_BASE}/Booking/active`);
        console.log('Response status:', response.status);

        if (response.ok) {
            const bookings = await response.json();
            console.log('Bookings received:', bookings);

            if (bookings.length === 0) {
                listDiv.innerHTML = '<p class="text-muted">No active bookings at the moment</p>';
                return;
            }

            let html = '<div class="table-responsive"><table class="table table-striped">';
            html += '<thead><tr><th>Booking ID</th><th>Student</th><th>Email</th><th>Exam Type</th><th>Status</th><th>Paid</th><th>Created</th></tr></thead><tbody>';

            bookings.forEach(booking => {
                // ВИПРАВЛЕНО: Передаємо дані про призначеного екзаменатора
                const statusBadge = getDetailedStatusBadge(
                    booking.status,
                    booking.assignedExaminerEmail,
                    booking.assignedExaminerName
                );

                const paidBadge = booking.isPaid
                    ? '<span class="badge bg-success">Paid</span>'
                    : '<span class="badge bg-warning">Pending</span>';

                const bookingJson = JSON.stringify({
                    bookingId: booking.bookingId,
                    studentName: booking.studentName,
                    studentEmail: booking.studentEmail,
                    status: booking.status,
                    assignedExaminerEmail: booking.assignedExaminerEmail,
                    assignedExaminerName: booking.assignedExaminerName
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
                    </tr>
                `;
            });

            html += '</tbody></table></div>';
            listDiv.innerHTML = html;
        } else {
            listDiv.innerHTML = '<div class="alert alert-danger">Failed to load bookings</div>';
        }
    } catch (error) {
        console.error('Error loading bookings:', error);
        listDiv.innerHTML = '<div class="alert alert-danger">Network error</div>';
    }
}

// ОНОВЛЕНА ФУНКЦІЯ - підтримка як текстових, так і числових статусів + Free/Taken логіка
function getDetailedStatusBadge(status, assignedExaminerEmail = null, assignedExaminerName = null) {
    // Мапінг числових значень до текстових
    const statusMap = {
        0: 'Created',
        1: 'PaymentPending',
        2: 'PaymentConfirmed',
        3: 'ExaminersContacted',
        4: 'ExaminerAssigned',
        5: 'Scheduled',
        6: 'Completed',
        7: 'Cancelled',
        8: 'RefundRequested',
        9: 'Refunded'
    };

    const statusText = typeof status === 'number' ? statusMap[status] : status;

    // ВИПРАВЛЕНО: Перевіряємо чи є призначений екзаменатор
    const hasAssignedExaminer = assignedExaminerEmail || assignedExaminerName;

    // Якщо є призначений екзаменатор - букінг взятий
    if (hasAssignedExaminer) {
        return '<span class="badge bg-danger">Taken</span>';
    }

    // Інакше перевіряємо статус
    if (statusText === 'Created' || statusText === 'PaymentPending' ||
        statusText === 'PaymentConfirmed' || statusText === 'ExaminersContacted') {
        return '<span class="badge bg-success">Free</span>';
    } else if (statusText === 'ExaminerAssigned' || statusText === 'Scheduled' || statusText === 'Completed') {
        return '<span class="badge bg-danger">Taken</span>';
    } else if (statusText === 'Cancelled') {
        return '<span class="badge bg-secondary">Cancelled</span>';
    } else if (statusText === 'Refunded' || statusText === 'RefundRequested') {
        return '<span class="badge bg-secondary">Refunded</span>';
    } else {
        return `<span class="badge bg-light text-dark">${statusText || status}</span>`;
    }
}

// ОНОВЛЕНА ФУНКЦІЯ fillExaminerForm - враховує призначеного екзаменатора
function fillExaminerForm(booking) {
    const statusMap = {
        0: 'Created',
        1: 'PaymentPending',
        2: 'PaymentConfirmed',
        3: 'ExaminersContacted',
        4: 'ExaminerAssigned',
        5: 'Scheduled',
        6: 'Completed',
        7: 'Cancelled',
        8: 'RefundRequested',
        9: 'Refunded'
    };

    const statusText = typeof booking.status === 'number' ? statusMap[booking.status] : booking.status;

    // ВИПРАВЛЕНО: Перевіряємо як статус, так і призначеного екзаменатора
    const hasAssignedExaminer = booking.assignedExaminerEmail || booking.assignedExaminerName;
    const isTaken = hasAssignedExaminer ||
        statusText === 'ExaminerAssigned' ||
        statusText === 'Scheduled' ||
        statusText === 'Completed';

    if (isTaken) {
        const examinerInfo = hasAssignedExaminer ?
            ` (Assigned to: ${booking.assignedExaminerName || booking.assignedExaminerEmail})` : '';
        alert(`This booking is already taken by another examiner.${examinerInfo}`);
        return;
    }

    // Решта коду залишається без змін
    document.getElementById('bookingId').value = booking.bookingId;
    document.getElementById('studentName').value = booking.studentName;
    document.getElementById('studentEmail').value = booking.studentEmail;

    const submitBtn = document.querySelector('#examinerResponseForm button[type="submit"]');
    if (isTaken) {
        submitBtn.disabled = true;
        submitBtn.innerHTML = '<i class="bi bi-lock"></i> Booking Taken';
        submitBtn.classList.add('btn-secondary');
        submitBtn.classList.remove('btn-info');
    } else {
        submitBtn.disabled = false;
        submitBtn.innerHTML = 'Submit Response';
        submitBtn.classList.add('btn-info');
        submitBtn.classList.remove('btn-secondary', 'btn-success');
    }

    const formHeader = document.querySelector('.card-header.bg-gradient-info');
    if (formHeader) {
        formHeader.scrollIntoView({
            behavior: 'smooth',
            block: 'start'
        });
    }

    const form = document.querySelector('#examinerResponseForm');
    if (form && form.parentElement) {
        form.parentElement.style.boxShadow = '0 0 20px rgba(0,123,255,0.5)';
        form.parentElement.style.transition = 'box-shadow 0.3s ease';
        setTimeout(() => {
            form.parentElement.style.boxShadow = '';
        }, 1500);
    }
}

async function loadFilteredBookings() {
    const email = document.getElementById('examinerEmailFilter').value;
    const examType = document.getElementById('examTypeFilter').value;
    const state = document.getElementById('stateFilter').value;
    const dateFrom = document.getElementById('dateFromFilter').value;

    if (!email) {
        alert('Please enter your email address');
        return;
    }

    const params = new URLSearchParams();
    if (email) params.append('examinerEmail', email);
    if (examType) params.append('examType', examType);
    if (state) params.append('state', state);
    if (dateFrom) params.append('dateFrom', dateFrom);

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

            let html = '<div class="table-responsive"><table class="table table-hover">';
            html += `<thead>
                <tr>
                    <th>Booking ID</th>
                    <th>Student</th>
                    <th>Exam Type</th>
                    <th>Location</th>
                    <th>Preferred Date</th>
                    <th>Days Waiting</th>
                    <th>Action</th>
                </tr>
            </thead><tbody>`;

            bookings.forEach(booking => {
                let dateDisplay = new Date(booking.preferredDate).toLocaleDateString();
                if (booking.startDate && booking.endDate) {
                    const start = new Date(booking.startDate).toLocaleDateString();
                    const end = new Date(booking.endDate).toLocaleDateString();
                    dateDisplay = `${start} - ${end}`;
                }

                const willingToTravelBadge = booking.willingToTravel
                    ? '<span class="badge bg-success">Yes</span>'
                    : '<span class="badge bg-secondary">No</span>';

                html += `
                    <tr>
                        <td><code>${booking.bookingId}</code></td>
                        <td>${booking.studentName}</td>
                        <td><span class="badge bg-info">${booking.examType}</span></td>
                        <td>${booking.location}</td>
                        <td>${dateDisplay}</td>
                        <td>${willingToTravelBadge}</td>
                        <td><span class="badge ${booking.daysWaiting > 3 ? 'bg-warning' : 'bg-secondary'}">${booking.daysWaiting} days</span></td>
                        <td>
                            <button class="btn btn-sm btn-success" 
                                onclick="fillResponseForm('${booking.bookingId}', '${booking.studentName.replace(/'/g, "\\'")}')">
                                Respond
                            </button>
                        </td>
                    </tr>`;
            });

            html += '</tbody></table></div>';
            listDiv.innerHTML = html;
        } else {
            listDiv.innerHTML = '<div class="alert alert-danger">Failed to load bookings</div>';
        }
    } catch (error) {
        listDiv.innerHTML = '<div class="alert alert-danger">Network error</div>';
    }
}

function fillResponseForm(bookingId, studentName) {
    document.getElementById('bookingId').value = bookingId;
    document.getElementById('studentName').value = studentName;
    document.getElementById('examinerEmail').focus();

    // Scroll to response form
    document.querySelector('.card-header.bg-gradient-info').scrollIntoView({ behavior: 'smooth' });
}

// Toggle date range visibility
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

// Експортуємо функції в глобальний scope
window.loadActiveBookings = loadActiveBookings;
window.loadFilteredBookings = loadFilteredBookings;
window.fillResponseForm = fillResponseForm;
window.fillExaminerForm = fillExaminerForm;
window.createNewBooking = createNewBooking;
window.retryBooking = retryBooking;
window.showExaminerPanel = showExaminerPanel;
window.toggleDateRange = toggleDateRange;