var osmosis = require('osmosis'),
	fs = require('fs'),
	users = require('./sp_users.json'),
	async = require('async'),
	impactDataPath = 'sp_users_impact.json';

if (!fs.existsSync(impactDataPath)) {
	fs.writeFileSync(impactDataPath, JSON.stringify([]));
}

osmosis.config('ignore_http_errors', true);
//osmosis.config('proxy', '91.195.183.54:3128');

var usersImpactData = require(`./${impactDataPath}`);

//users = users.slice(0, 10);
var total = users.length;

async.eachOfSeries(users, function (user, indx, callback) {
	console.log(`Parsing user ${user.user_id}. ${indx} out of ${total}`);

	if (indx % 100 === 0) {
		console.log("Write values")
		fs.writeFileSync(impactDataPath, JSON.stringify(usersImpactData));
	}
	var userParsed = usersImpactData.filter(function (data) {
		return data.user_id === user.user_id;
	}).length === 1;

	if (userParsed) {
		console.log(`Skipping, impact found for user: ${user.user_id}`);
		setTimeout(callback, 10);
	} else {
		handleUser(user, callback);
	}
}, function done() {
	console.log('ready');
	fs.writeFileSync(impactDataPath, JSON.stringify(usersImpactData));
});

function handleUser(user, callback) {
	osmosis
		.get('http://sharepoint.stackexchange.com/users/' + user.user_id)
		.set({
			'impact': '#user-card .grid--cell.fl1 .grid--cell.fl-shrink0.pr24 .fc-medium.mb16 .grid--cell:nth-child(3) .grid--cell:nth-child(1)',
			'title': '#user-card .grid--cell.fl1 .profile-user--about .grid--cell h2 .grid--cell:nth-child(1)'
		})
		.data(function (data) {
			if (!data.impact) {
				usersImpactData.push({
					user_id: user.user_id,
					impact: -1,
					raw: ''
				});
				console.log('no impact data for user: ' + data.title);
				setTimeout(callback, 2000);
				return;
			}

			data.impact_raw = data.impact;
			if (data.impact.startsWith('~')) {
				data.impact = data.impact.substring(1, data.impact.length);
			}

			if (data.impact.endsWith('k')) {
				data.impact = data.impact.substr(0, data.impact.length - 1);
				data.impact = parseFloat(data.impact) * 1000;
			} else if (data.impact.endsWith('m')) {
				data.impact = data.impact.substr(0, data.impact.length - 1);
				data.impact = parseFloat(data.impact) * 1000000;
			} else {
				data.impact = parseFloat(data.impact);
			}

			usersImpactData.push({
				user_id: user.user_id,
				impact: data.impact,
				raw: data.impact_raw
			});
			console.log('user: ' + user.user_id + ' impact: ' + data.impact);

			setTimeout(callback, 2000);
		})
		.error(console.log);
}
