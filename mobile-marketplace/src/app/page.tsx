import React from 'react';

const apps = [
  { id: 1, name: 'Quantum Task', category: 'Productivity', rating: 4.9, downloads: '10K+', description: 'Next-gen AI task management.', icon: 'https://cdn.pixabay.com/photo/2021/08/25/20/42/field-6574455__340.jpg' },
  { id: 2, name: 'Nebula Chat', category: 'Social', rating: 4.7, downloads: '50K+', description: 'Secure, interstellar messaging.', icon: 'https://cdn.pixabay.com/photo/2016/11/29/12/13/fence-1869401__340.jpg' },
  { id: 3, name: 'Pixel Studio', category: 'Design', rating: 4.8, downloads: '5K+', description: 'Professional mobile photo editor.', icon: 'https://cdn.pixabay.com/photo/2017/02/08/17/24/fantasy-2049567__340.jpg' },
  { id: 4, name: 'Zen Flow', category: 'Health', rating: 5.0, downloads: '20K+', description: 'Mindfulness and meditation.', icon: 'https://cdn.pixabay.com/photo/2016/11/29/13/08/field-1869501__340.jpg' },
];

export default function Marketplace() {
  return (
    <div className="min-h-screen pb-20">
      {/* Header */}
      <header className="glass-panel sticky top-0 z-50 px-8 py-4 flex items-center justify-between">
        <div className="flex items-center gap-3">
          <div className="w-10 h-10 bg-primary rounded-xl flex items-center justify-center shadow-lg shadow-indigo-500/20">
            <svg className="w-6 h-6 text-white" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M16 11V7a4 4 0 00-8 0v4M5 9h14l1 12H4L5 9z" />
            </svg>
          </div>
          <h1 className="text-xl font-bold tracking-tight">Syncro <span className="text-primary">Mobile Store</span></h1>
        </div>
        <div className="flex items-center gap-6">
          <nav className="hidden md:flex items-center gap-8 text-sm font-medium text-secondary">
            <a href="#" className="text-foreground transition-colors">Discover</a>
            <a href="#" className="hover:text-foreground transition-colors">Apps</a>
            <a href="#" className="hover:text-foreground transition-colors">Games</a>
            <a href="#" className="hover:text-foreground transition-colors">My Library</a>
          </nav>
          <button className="btn-primary">Update All</button>
        </div>
      </header>

      <main className="max-w-7xl mx-auto px-8 pt-12">
        {/* Hero */}
        <section className="mb-20">
          <div className="relative rounded-[40px] overflow-hidden p-12 bg-gradient-to-br from-indigo-900/40 to-slate-900/40 border border-white/5">
            <div className="max-w-2xl relative z-10">
              <span className="badge mb-6 inline-block">New Release</span>
              <h2 className="text-5xl font-extrabold mb-6 leading-tight">Elevate Your <span className="gradient-text">Syncro Experience</span></h2>
              <p className="text-lg text-secondary mb-10 leading-relaxed">Browse thousands of premium mobile applications optimized for your ecosystem. Faster, safer, and smarter.</p>
              <div className="flex gap-4">
                <button className="btn-primary text-lg px-10 py-4">Explore Trending</button>
                <button className="px-10 py-4 rounded-full border border-white/10 font-semibold hover:bg-white/5 transition-all">Learn More</button>
              </div>
            </div>
            {/* Abstract Background Element */}
            <div className="absolute top-[-20%] right-[-10%] w-[600px] h-[600px] bg-primary/20 blur-[120px] rounded-full"></div>
          </div>
        </section>

        {/* Categories */}
        <section className="mb-16">
          <div className="flex items-center justify-between mb-8">
            <h3 className="text-2xl font-bold">Featured Categories</h3>
            <a href="#" className="text-sm font-semibold text-primary hover:underline">View All</a>
          </div>
          <div className="grid grid-cols-2 md:grid-cols-4 lg:grid-cols-6 gap-4">
            {['Productivity', 'Social', 'Gaming', 'Finance', 'Education', 'Lifestyle'].map(cat => (
              <div key={cat} className="glass-panel p-6 rounded-2xl text-center cursor-pointer hover:bg-white/5 transition-all group">
                <div className="w-12 h-12 bg-white/5 rounded-full mx-auto mb-4 flex items-center justify-center group-hover:scale-110 transition-transform">
                  <div className="w-2 h-2 bg-primary rounded-full"></div>
                </div>
                <span className="text-sm font-semibold">{cat}</span>
              </div>
            ))}
          </div>
        </section>

        {/* Apps Grid */}
        <section>
          <div className="flex items-center justify-between mb-8">
            <h3 className="text-2xl font-bold">Top Applications</h3>
            <div className="flex gap-2">
              <button className="p-2 rounded-lg bg-white/5 border border-white/10 hover:bg-white/10"><svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M15 19l-7-7 7-7" /></svg></button>
              <button className="p-2 rounded-lg bg-white/5 border border-white/10 hover:bg-white/10"><svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M9 5l7 7-7 7" /></svg></button>
            </div>
          </div>
          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-6">
            {apps.map(app => (
              <div key={app.id} className="card group">
                <div className="relative mb-6">
                  <div className="w-20 h-20 rounded-2xl overflow-hidden shadow-2xl group-hover:scale-105 transition-transform duration-500">
                    <img src={app.icon} alt={app.name} className="w-full h-full object-cover" />
                  </div>
                  <div className="absolute top-0 right-0">
                    <span className="badge">FREE</span>
                  </div>
                </div>
                <div className="mb-6">
                  <h4 className="text-lg font-bold mb-1">{app.name}</h4>
                  <p className="text-xs text-primary font-bold uppercase tracking-wider mb-3">{app.category}</p>
                  <p className="text-sm text-secondary line-clamp-2">{app.description}</p>
                </div>
                <div className="flex items-center justify-between mt-auto">
                  <div className="flex items-center gap-2">
                    <div className="flex items-center">
                      {[1, 2, 3, 4, 5].map(i => (
                        <svg key={i} className={`w-3 h-3 ${i <= Math.floor(app.rating) ? 'text-yellow-400' : 'text-slate-700'}`} fill="currentColor" viewBox="0 0 20 20">
                          <path d="M9.049 2.927c.3-.921 1.603-.921 1.902 0l1.07 3.292a1 1 0 00.95.69h3.462c.969 0 1.371 1.24.588 1.81l-2.8 2.034a1 1 0 00-.364 1.118l1.07 3.292c.3.921-.755 1.688-1.54 1.118l-2.8-2.034a1 1 0 00-1.175 0l-2.8 2.034c-.784.57-1.838-.197-1.539-1.118l1.07-3.292a1 1 0 00-.364-1.118L2.98 8.72c-.783-.57-.38-1.81.588-1.81h3.461a1 1 0 00.951-.69l1.07-3.292z" />
                        </svg>
                      ))}
                    </div>
                    <span className="text-xs font-bold">{app.downloads}</span>
                  </div>
                  <button className="w-8 h-8 rounded-full bg-white/5 flex items-center justify-center hover:bg-primary transition-colors">
                    <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M12 4v16m8-8H4" />
                    </svg>
                  </button>
                </div>
              </div>
            ))}
          </div>
        </section>
      </main>
      
      {/* Footer Mobile Nav (Glass) */}
      <div className="md:hidden fixed bottom-6 left-1/2 -translate-x-1/2 w-[90%] glass-panel rounded-2xl p-4 flex justify-around items-center">
        <div className="text-primary"><svg className="w-6 h-6" fill="currentColor" viewBox="0 0 20 20"><path d="M10.707 2.293a1 1 0 00-1.414 0l-7 7a1 1 0 001.414 1.414L4 10.414V17a1 1 0 001 1h2a1 1 0 001-1v-2a1 1 0 011-1h2a1 1 0 011-1v2a1 1 0 001 1h2a1 1 0 001-1v-6.586l.293.293a1 1 0 001.414-1.414l-7-7z" /></svg></div>
        <div className="text-secondary"><svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" /></svg></div>
        <div className="text-secondary"><svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M16 11V7a4 4 0 00-8 0v4M5 9h14l1 12H4L5 9z" /></svg></div>
        <div className="text-secondary"><svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14a7 7 0 00-7-7z" /></svg></div>
      </div>
    </div>
  );
}
