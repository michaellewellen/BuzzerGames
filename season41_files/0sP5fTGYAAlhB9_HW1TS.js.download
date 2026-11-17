$(document).ready(function() {
	"use strict";
	
	var gas = pb.plugin.get('google_analytics').settings,
		_gaq = [],
		methods = {
			'UNIVERSAL' : 0,
			'CLASSIC'   : 1,
			'DUAL_TAG'  : 2
		},
		default_domain = 'auto',
		delay = 500;
	
	// Get the Analytics domain setting
	function getDomain()
	{
		var d = default_domain;
		if (d == 'auto') { return d; }
		d = d.toLowerCase().replace(/http[s]*:\/\/(www\.)*(.+?)[\/]*.*/, '$2');				// remove leading and trailing junk
		if (!/^([a-z0-9]+\.)?[a-z0-9][a-z0-9\-]+(\.[a-z]{2,6}){1,}$/.test(d)) { d = ''; }	// validate domain or empty on failure
		if (d == '') { return default_domain; }
		return d;
	}

	// Guests must pass Captcha
	function passCaptcha()
	{
		if (!proboards.data('captcha_init') || !proboards.data('is_current_user_guest')) { return true; }

		var errors = proboards.data('captcha_init').captcha_errors;
		return (!errors) ? true : false;
	}
	
	// Check for form errors
	function validForm()
	{
		return (!$('.ui-form-error').length || $('.ui-form-error').css('display') == 'none') ? true : false;
	}
	
	// Check for valid event source
	function validSource()
	{
		if (!sessionStorage || !sessionStorage.getItem('referrer')) { return false; }
		
		var source = sessionStorage.getItem('referrer');
		var path   = /\x2F((register)(\x2Fsubmit)*|((thread|post)\x2F(create|new)\x2F\d+)|(thread\x2F\d+?.*)|(post\x2F\d+?\x2Fquote\x2F\d+?.*))$/;
		return path.test(source);
	}
	
	// Set last event
	function queueEvent(data)
	{
        if (data) { sessionStorage.setItem('event', JSON.stringify(data)); }
	}
	
	// Send to Google
	function sendEvent(data)
	{
		// Universal Analytics
		if (gas.method == methods.UNIVERSAL || gas.method == methods.DUAL_TAG) {
			ga('send', data);
		}

		// Classic Analytics
		if (gas.method == methods.CLASSIC || (gas.method == methods.DUAL_TAG && gas.dual_tracking_id != ''))
		{
			_gaq.push([ '_trackEvent', data.eventCategory, data.eventAction, data.eventLabel, parseInt(data.eventValue, 10) ]);
		}
	}
	
	// Process queued event
	function processQueue()
	{
		if (!sessionStorage || !sessionStorage.getItem('event')) { return };
		
        var data = JSON.parse(sessionStorage.getItem('event'));

		if (data && passCaptcha())
		{
			// Validate queued event
			if (validForm() && validSource()) {
				sendEvent(data);
			}

			// Done or user navigated away
			sessionStorage.removeItem('event');
		}
	}

	// Insert tracking code
	if (!gas.custom_code)
	{
		// Universal Analytics
		if (gas.method == methods.UNIVERSAL || gas.method == methods.DUAL_TAG)
		{
			(function(i,s,o,g,r,a,m){i['GoogleAnalyticsObject']=r;i[r]=i[r]||function(){(i[r].q=i[r].q||[]).push(arguments)},i[r].l=1*new Date();a=s.createElement(o),m=s.getElementsByTagName(o)[0];a.async=1;a.src=g;m.parentNode.insertBefore(a,m)})(window,document,'script','//www.google-analytics.com/analytics.js','ga');

			ga('create', gas.id, getDomain());
			ga('send', 'pageview');
		}

		// Classic Analytics
		if (gas.method == methods.CLASSIC || (gas.method == methods.DUAL_TAG && gas.dual_tracking_id != ''))
		{
			_gaq.push([ '_setAccount', (gas.method == methods.DUAL_TAG ? gas.dual_tracking_id : gas.id) ]);
			_gaq.push([ '_setDomainName', getDomain() ]);
			_gaq.push([ '_trackPageview' ]);

			(function(){var e=document.createElement('script');e.type='text/javascript';e.async=true;e.src=('https:'==document.location.protocol?'https://ssl':'http://www')+'.google-analytics.com/ga.js';var t=document.getElementsByTagName('script')[0];t.parentNode.insertBefore(e,t)}());
		}
	}
	else {
		// User Specified
		var gac = gas.custom_code;
		
		// Give priority to plugin settings when default Google snippets are pasted here
		var ga_create = 'ga(\'create\', \'' + gas.id + '\', \'' + getDomain() + '\')';
		var ga_create_classic_id = '_gaq.push([ \'_setAccount\', \'' + (gas.method == methods.DUAL_TAG ? gas.dual_tracking_id : gas.id) + '\' ]);';
		var ga_create_classic_dn = '_gaq.push([ \'_setDomainName\', \'' + getDomain() + '\' ]);';
		
		gac = gac.replace(/<[\/]*script>/, '');
		gac = gac.replace(/<\!--.*?-->/, '');
		gac = gac.replace(/\bga\('create', '.*', '.*'\)/, ga_create);
		gac = gac.replace(/\b_gaq\.push\(\[ '_setAccount', '.*' \]\)/, ga_create_classic_id);
		gac = gac.replace(/\b_gaq\.push\(\[ '_setDomainName', '.*' \]\)/, ga_create_classic_dn);
		
		// We're not in Kansas anymore, Toto.
		eval(gac);
	}
	
	// Process queued events
	setTimeout(function() { processQueue(); }, delay);
	
	// New Thread
	if (gas.threads)
	{
		if (gas.thread_category == '') { gas.thread_category = 'thread'; }
		if (gas.thread_action == '')   { gas.thread_category = 'create'; }
		
		$('.form_thread_new').submit(function() {
			var data = {
				hitType:       'event',
				eventCategory: gas.thread_category,
				eventAction:   gas.thread_action,
				eventLabel:    gas.thread_label,
				eventValue:    gas.thread_value
			};
			queueEvent(data);
		});
	}
	
	// Posts and Quick Replies
	if (gas.posts)
	{
		if (gas.post_category == '') { gas.post_category = 'post'; }
		if (gas.post_action == '')   { gas.post_category = 'create'; }
		
		$('.form_post_new').submit(function() {
			var data = {
				hitType:       'event',
				eventCategory: gas.post_category,
				eventAction:   gas.post_action,
				eventLabel:    gas.post_label,
				eventValue:    gas.post_value
			};
			queueEvent(data);
		});

		$('.form_post_quick_reply').submit(function() {
			var data = {
				hitType:       'event',
				eventCategory: gas.post_category,
				eventAction:   gas.post_action,
				eventLabel:    gas.post_label,
				eventValue:    gas.post_value
			};
			queueEvent(data);
		});
	}
	
	// New User Registrations
	if (gas.users)
	{
		if (gas.register_category == '') { gas.register_category = 'user'; }
		if (gas.register_action == '')   { gas.register_category = 'register'; }
		
		$('.form_registration_register').submit(function() {
			var data = {
				hitType:       'event',
				eventCategory: gas.register_category,
				eventAction:   gas.register_action,
				eventLabel:    gas.register_label,
				eventValue:    gas.register_value
			};
			queueEvent(data);
		});
	}
});
