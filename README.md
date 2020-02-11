# RestProxy
Reflection-based REST client for .NET, using `HttpClient`.

## Why?

I started working on this little tool back around 2013 for a project that relied on REST API. 
By that time similar libraries, like [Retrofit](http://square.github.io/retrofit) were not
fit for the purpose, or simply lacked an important feature.
It is also much more interesting to develop such a tool than simply use an existing one,
at least I enjoyed it very much. :wink:

Since 2013 multiple nice libraries have emerged which are more complete than my solution. 
If you are looking for an easy REST client with reflection-based API, I suggest you
checked out the very nice [RestEase](https://github.com/canton7/RestEase) project.

## Features

* reflection-based API
* supports method parameters in:
** body (multipart and form-encoded are also supported)
** query parameters
** path placeholders
** headers
* default parameter support
* Synchronous and asynchronous (Task-based) return values
* path concatenation: base path can be provided in `HttpClient`, subpath in the interface header, and additional path at the method
* CancellationToken support
* Stream support (can be used to upload/download files, even multipart)
* customizable error handling
