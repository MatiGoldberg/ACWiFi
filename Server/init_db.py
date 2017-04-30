from acpage import init_db

def main():
	print '>> Starting init_db...'
	try:
		init_db()
		print '>> success'
	except:
		print '>> Failed.'

if (__name__ == '__main__'):
	main()