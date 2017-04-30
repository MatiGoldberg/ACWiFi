import os
import sqlite3
from flask import Flask, request, session, g, redirect, url_for, abort, \
    render_template, flash
from time import asctime, gmtime, time, strftime

# -- state parameters -- #
system_state = "OFF"
fan_state = "3"
new_command = False
system_temp = 15
got_ack_from_netduino = False
start_time = 0
show_on_time = False
last_seen_on = "[---]"
# ---------------------- #

# -- defines ----------- #
TIMEZONE_SHIFT_SEC = 3*60*60
# ---------------------- #


# create our little application :)
app = Flask(__name__)
app.config.from_object(__name__)

# Load default config and override config from an environment variable
app.config.update(dict(
    DATABASE=os.path.join(app.root_path, 'acpage.db'),
    DEBUG=False,
    SECRET_KEY='development key',
    USERNAME='lola',
    PASSWORD='thecat'
))
app.config.from_envvar('REMOTE_SETTINGS', silent=True)

# --- Database functions ------------------------------------------- #
def connect_db():
    """Connects to the specific database."""
    rv = sqlite3.connect(app.config['DATABASE'])
    rv.row_factory = sqlite3.Row
    return rv

def get_db():
    """Opens a new database connection if there is none yet for the
    current application context.
    """
    if not hasattr(g, 'sqlite_db'):
        g.sqlite_db = connect_db()
    return g.sqlite_db

@app.teardown_appcontext
def close_db(error):
    """Closes the database again at the end of the request."""
    if hasattr(g, 'sqlite_db'):
        g.sqlite_db.close()


# --- Time Functions ---------------------------------------------------- #
def get_time():
    return asctime(gmtime(time() + TIMEZONE_SHIFT_SEC))


# After command is received from system
def set_elapsed_time():
    global system_state, start_time
    if system_state == "ON":
        # start timer
        start_time = time()


def get_on_time():
    global show_on_time, start_time

    if not show_on_time:
        return "---"

    elapsed_time = time() - start_time
    if elapsed_time > 3600:
        return strftime('%Hh%Mm%Ss',gmtime(elapsed_time))
    elif elapsed_time > 60:
        return strftime('%M minutes, %S seconds',gmtime(elapsed_time))

    return strftime('%S sec',gmtime(elapsed_time))

# --- View functions ---------------------------------------------------- #
@app.route('/')
def show_entries():
    global system_state, got_ack_from_netduino, system_temp
    db = get_db()
    cur = db.execute('select ttime, temp from entries order by id desc')
    entries = cur.fetchall()
    if len(entries) > 5:
        entries = entries[0:5]

    if got_ack_from_netduino:
        flash('Command received by system.');
        got_ack_from_netduino = False
        set_elapsed_time()

    ischecked = ''
    if system_state == 'ON':
        ischecked = 'checked'

    if len(entries) == 0:
        room_temp = '25'
        room_temp_time_stamp = 'data unavailable'
    else:
        room_temp = entries[0][1]
        room_temp_time_stamp = entries[0][0]

    system_on_time = get_on_time()

    return render_template('show_entries.html', sys_temp=str(system_temp), sys_state=system_state, entries=entries,
                           isChecked=ischecked, room_temp=room_temp, room_temp_time_stamp=room_temp_time_stamp, 
                           on_time = system_on_time, last_seen_on = last_seen_on)


@app.route('/poststate', methods=['POST'])
def post_state():
    global system_state, new_command, system_temp
    if not session.get('logged_in'):
        abort(401)

    if 'onoffswitch' in request.form:
        system_state = 'ON'
    else:
        system_state = 'OFF'

    if 'temperature' in request.form:
        system_temp = int(request.form['temperature'])

    new_command = True
    flash('System state and temperature set')

    return redirect(url_for('show_entries'))


@app.route('/login', methods=['GET', 'POST'])
def login():
    error = None
    if request.method == 'POST':
        if request.form['username'] != app.config['USERNAME']:
            error = 'Invalid username'
        elif request.form['password'] != app.config['PASSWORD']:
            error = 'Invalid password'
        else:
            session['logged_in'] = True
            flash('You were logged in')
            return redirect(url_for('show_entries'))
    return render_template('login.html', error=error)


@app.route('/logout')
def logout():
    session.pop('logged_in', None)
    flash('You were logged out')
    return redirect(url_for('show_entries'))


# --- Netduino interface ---------------------- #
@app.route('/extpost/<temp_value>/<msg_id>')
def post_temp(msg_id, temp_value):
    global last_seen_on
    last_seen_on = get_time()

    db = get_db()
    db.execute('insert into entries (ttime, temp) values (?, ?)', [get_time(), float(temp_value)])
    db.commit()

    app.logger.debug('Netduino post {#' + msg_id + '}')
    return "Posted."


@app.route('/extget/<msg_id>')
def get_command(msg_id):
    global new_command, system_state, system_temp, last_seen_on, fan_state
    last_seen_on = get_time()

    if (new_command == True):
        return_string = "1#"
    else:
        return_string = "0#"

    return_string = return_string + system_state + "#" + str(system_temp) + "#" + fan_state

    app.logger.debug('Netduino get {#' + msg_id + '}')
    return return_string


@app.route('/getack/<msg_id>')
def get_ack(msg_id):
    global new_command, got_ack_from_netduino, system_state, show_on_time
    new_command = False
    got_ack_from_netduino = True

    if system_state == "ON":
        show_on_time = True
    else:
        show_on_time = False

    app.logger.debug('Netduino post ack {#' + msg_id + '}')
    return "Posted."